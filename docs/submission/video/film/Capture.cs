/// <summary>Taking a picture of the app's main window, and letting it run between pictures.</summary>
internal static class Capture
{
    /// <summary>Lets the app handle its messages (timers, paints, replies from the service) for a stretch of real time.</summary>
    public static void Pump(double seconds)
    {
        var until = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < until)
        {
            Application.DoEvents();
            Thread.Sleep(8);
        }
    }

    /// <summary>
    ///  A picture of the main window's client area. DrawToBitmap paints sibling controls back
    ///  to front the wrong way round, so the controls that float over the page are drawn again on top.
    /// </summary>
    public static Bitmap Window(Form form, params Control[] floating)
    {
        var bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height);
        using var whole = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(whole, new Rectangle(Point.Empty, form.Size));
        var origin = form.PointToScreen(Point.Empty);
        using var g = Graphics.FromImage(bitmap);
        g.DrawImage(whole, -(origin.X - form.Left), -(origin.Y - form.Top));
        foreach (var control in floating)
        {
            if (!control.Visible || control.Width <= 0 || control.Height <= 0)
            {
                continue;
            }
            using var part = new Bitmap(control.Width, control.Height);
            control.DrawToBitmap(part, new Rectangle(Point.Empty, control.Size));
            g.DrawImage(part, control.Location);
        }
        return bitmap;
    }
}
