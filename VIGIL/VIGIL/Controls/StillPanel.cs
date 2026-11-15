namespace VIGIL.Controls
{
    /// <summary>
    ///  A scrolling panel that stays where it is when something inside it takes the focus.
    ///  A plain panel scrolls the focused control to the top, so clicking a row or a button
    ///  part-way down would move everything under the mouse.
    /// </summary>
    [System.ComponentModel.DesignerCategory("Code")]
    public class StillPanel : Panel
    {
        public StillPanel()
        {
            AutoScroll = true;
        }

        protected override Point ScrollToControl(Control activeControl) => DisplayRectangle.Location;
    }
}
