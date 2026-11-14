namespace VIGIL.Forms
{
    partial class MainForm
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.navRail = new VIGIL.Controls.NavRail();
            this.contentHost = new System.Windows.Forms.Panel();
            this.SuspendLayout();
            //
            // navRail
            //
            this.navRail.Dock = System.Windows.Forms.DockStyle.Left;
            this.navRail.Location = new System.Drawing.Point(0, 0);
            this.navRail.Name = "navRail";
            this.navRail.Size = new System.Drawing.Size(240, 800);
            this.navRail.TabIndex = 0;
            //
            // contentHost
            //
            this.contentHost.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(19)))), ((int)(((byte)(20)))), ((int)(((byte)(21)))));
            this.contentHost.Dock = System.Windows.Forms.DockStyle.Fill;
            this.contentHost.Location = new System.Drawing.Point(240, 0);
            this.contentHost.Name = "contentHost";
            this.contentHost.Padding = new System.Windows.Forms.Padding(40, 32, 16, 0);
            this.contentHost.Size = new System.Drawing.Size(1040, 800);
            this.contentHost.TabIndex = 1;
            //
            // MainForm
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            this.ClientSize = new System.Drawing.Size(1280, 800);
            this.Controls.Add(this.contentHost);
            this.Controls.Add(this.navRail);
            this.MinimumSize = new System.Drawing.Size(1024, 680);
            this.Name = "MainForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "VIGIL";
            this.ResumeLayout(false);
        }

        #endregion

        private VIGIL.Controls.NavRail navRail;
        private System.Windows.Forms.Panel contentHost;
    }
}
