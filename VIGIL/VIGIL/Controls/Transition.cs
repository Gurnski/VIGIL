namespace VIGIL.Controls
{
    /// <summary>
    ///  Eases a value towards a target on a UI timer and calls back after every step.
    ///  Controls use it for hover fades, sliding indicators and reveal animations.
    /// </summary>
    public sealed class Transition : IDisposable
    {
        private readonly System.Windows.Forms.Timer timer = new() { Interval = 15 };
        private readonly Action changed;
        private float target;

        public Transition(Action changed)
        {
            this.changed = changed;
            timer.Tick += (s, e) => Step();
        }

        public float Value { get; private set; }

        /// <summary>
        ///  Fraction of the remaining distance covered per tick, from 0 to 1. Higher is snappier.
        /// </summary>
        public float Speed { get; set; } = 0.2f;

        public bool IsRunning => timer.Enabled;

        public void To(float value)
        {
            target = value;
            if (!timer.Enabled)
            {
                timer.Start();
            }
        }

        /// <summary>
        ///  Sets the value immediately, with no animation.
        /// </summary>
        public void Jump(float value)
        {
            target = value;
            Value = value;
            timer.Stop();
            changed();
        }

        private void Step()
        {
            Value += (target - Value) * Speed;
            if (Math.Abs(target - Value) < 0.002f)
            {
                Value = target;
                timer.Stop();
            }
            changed();
        }

        public void Dispose() => timer.Dispose();
    }
}
