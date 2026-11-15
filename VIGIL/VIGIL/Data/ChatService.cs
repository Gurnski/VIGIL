using System.Text.Json;
using VIGIL.Models;

namespace VIGIL.Data
{
    /// <summary>
    ///  The chats with the Detective: the saved conversations, and sending a question and
    ///  recording its answer. One instance is shared by the floating panel and the Detective
    ///  page, so both always show the same thing.
    ///
    ///  Conversations are saved to <c>%LOCALAPPDATA%\VIGIL\chats.json</c>, outside the repo.
    ///  Call it from the UI thread: its events are raised on the thread that called it.
    /// </summary>
    public sealed class ChatService
    {
        private readonly DetectiveClient client;
        private readonly string path;
        private readonly List<Conversation> conversations = new();
        private readonly HashSet<string> waiting = new();

        public ChatService(DetectiveClient client, string? storePath = null)
        {
            this.client = client;
            path = storePath ?? Path.Combine(AppFolder.Path, "chats.json");
            Load();
        }

        /// <summary>Raised when a conversation gains a message, starts or stops waiting, or is renamed.</summary>
        public event EventHandler<Conversation>? ConversationChanged;

        /// <summary>Raised when a conversation is added or deleted.</summary>
        public event EventHandler? ListChanged;

        /// <summary>Raised with the answer when the service has answered a question. Not raised for a sample answer.</summary>
        public event EventHandler<ChatMessage>? Answered;

        /// <summary>
        ///  The example questions the chat offers. They start as the fixture's. When the app is
        ///  showing a real scan, the shell replaces them with questions that make sense of it.
        /// </summary>
        public IReadOnlyList<string> Suggestions { get; set; } = Fixture.Questions;

        /// <summary>
        ///  Whether a question the service cannot answer may get the fixture's canned answer,
        ///  marked as a sample. Only right while the app is showing the fixture: beside a real
        ///  scan, a made-up answer about made-up cells would mislead even with its mark.
        /// </summary>
        public bool AllowSamples { get; set; } = true;

        /// <summary>Saved conversations, most recently used first.</summary>
        public IReadOnlyList<Conversation> Conversations => conversations.OrderByDescending(c => c.Updated).ToList();

        public string ServiceAddress => client.Address;

        /// <summary>Whether the last question reached the service. Null until one has been asked.</summary>
        public bool? Connected { get; private set; }

        public bool IsWaiting(Conversation conversation) => waiting.Contains(conversation.Id);

        /// <summary>A new, empty conversation. It joins the saved list when its first question is asked.</summary>
        public Conversation Start() => new();

        public void Delete(Conversation conversation)
        {
            if (conversations.RemoveAll(c => c.Id == conversation.Id) > 0)
            {
                Save();
                ListChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        ///  Asks a question in a conversation. The question appears at once and the answer when
        ///  it arrives. If the service cannot be reached, an example question gets its canned
        ///  answer marked as a sample, and any other question gets a note saying so.
        /// </summary>
        public async Task AskAsync(Conversation conversation, string question, FindingSummary? about = null)
        {
            question = question.Trim();
            if (question.Length == 0 || IsWaiting(conversation))
            {
                return;
            }

            var request = new AskRequest
            {
                Question = question,
                History = HistoryOf(conversation),
                FindingId = about?.Id
            };

            bool isNew = conversations.All(c => c.Id != conversation.Id);
            if (isNew)
            {
                conversation.Title = question.Length > 60 ? question[..57].TrimEnd() + "…" : question;
                conversations.Add(conversation);
            }
            conversation.Messages.Add(new ChatMessage
            {
                Role = ChatRole.User,
                Text = question,
                ContextFindingId = about?.Id,
                ContextSummary = about == null ? null : about.KindLabel + " in cell " + about.CellId + ": " + about.Summary
            });
            conversation.Updated = DateTime.UtcNow;
            waiting.Add(conversation.Id);
            Save();
            if (isNew)
            {
                ListChanged?.Invoke(this, EventArgs.Empty);
            }
            ConversationChanged?.Invoke(this, conversation);

            ChatMessage answer;
            try
            {
                var response = await client.AskAsync(request);
                Connected = true;
                answer = FromResponse(response, AnswerSource.Service);
            }
            catch (Exception error)
            {
                // Whatever went wrong, the conversation must stop waiting, or its send button stays off for good.
                Connected = false;
                string reason = error is DetectiveUnavailableException ? error.Message : "Something went wrong asking the service.";
                var sample = AllowSamples ? Fixture.AnswerTo(question) : null;
                answer = sample != null
                    ? FromResponse(sample, AnswerSource.Sample)
                    : new ChatMessage
                    {
                        Role = ChatRole.Assistant,
                        Source = AnswerSource.NotConnected,
                        Text = reason + " The Detective answers from the analysis service at " + client.Address
                            + ", so there is no answer to show."
                            + (AllowSamples ? " The example questions still work, with sample answers."
                                : " It needs the service to hold a key for the language model.")
                    };
            }
            finally
            {
                waiting.Remove(conversation.Id);
            }

            // The conversation may have been deleted while the answer was on its way.
            if (conversations.Any(c => c.Id == conversation.Id))
            {
                conversation.Messages.Add(answer);
                conversation.Updated = DateTime.UtcNow;
                Save();
            }
            ConversationChanged?.Invoke(this, conversation);
            if (answer.Source == AnswerSource.Service)
            {
                Answered?.Invoke(this, answer);
            }
        }

        private static ChatMessage FromResponse(AskResponse response, AnswerSource source) => new()
        {
            Role = ChatRole.Assistant,
            Text = response.Answer,
            Status = response.Status,
            Citations = response.Citations,
            Limitations = response.Limitations,
            Model = response.Model,
            Steps = response.Steps,
            Source = source
        };

        /// <summary>
        ///  Earlier turns to send with the next question: only questions the service answered,
        ///  each with that answer. A question that failed is left out, so the service never sees
        ///  a user turn with no reply after it. Sample answers are left out too, because their
        ///  numbers are made up and must not reach the real service as if it had said them.
        /// </summary>
        private static List<AskTurn> HistoryOf(Conversation conversation)
        {
            var turns = new List<AskTurn>();
            var messages = conversation.Messages;
            for (int i = 0; i + 1 < messages.Count; i++)
            {
                var question = messages[i];
                var answer = messages[i + 1];
                if (question.Role == ChatRole.User && answer.Role == ChatRole.Assistant && answer.Source == AnswerSource.Service)
                {
                    turns.Add(new AskTurn { Role = "user", Text = question.Text });
                    turns.Add(new AskTurn { Role = "assistant", Text = answer.Text });
                    i++;
                }
            }
            return turns;
        }

        private void Load()
        {
            try
            {
                if (File.Exists(path))
                {
                    var saved = JsonSerializer.Deserialize<List<Conversation>>(File.ReadAllText(path), DetectiveClient.Json);
                    if (saved != null)
                    {
                        conversations.AddRange(saved.Where(c => c.Messages.Count > 0));
                    }
                }
            }
            catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException)
            {
                // A damaged or unreadable file should not stop the app. Start with no saved chats.
            }
        }

        private void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                string temporary = path + ".tmp";
                File.WriteAllText(temporary, JsonSerializer.Serialize(conversations, DetectiveClient.Json));
                File.Move(temporary, path, true);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // The chat still works for this session if it cannot be saved.
            }
        }
    }
}
