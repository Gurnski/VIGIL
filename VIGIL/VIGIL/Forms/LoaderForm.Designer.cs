namespace VIGIL.Forms
{
    partial class LoaderForm
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
            this.components = new System.ComponentModel.Container();
            this.logoPictureBox = new System.Windows.Forms.PictureBox();
            this.titleLabel = new System.Windows.Forms.Label();
            this.spinner = new VIGIL.Controls.SpinnerControl();
            this.taglineLabel = new System.Windows.Forms.Label();
            this.taglineTimer = new System.Windows.Forms.Timer(this.components);
            this.fadeTimer = new System.Windows.Forms.Timer(this.components);
            ((System.ComponentModel.ISupportInitialize)(this.logoPictureBox)).BeginInit();
            this.SuspendLayout();
            //
            // logoPictureBox
            //
            this.logoPictureBox.Location = new System.Drawing.Point(110, 30);
            this.logoPictureBox.Name = "logoPictureBox";
            this.logoPictureBox.Size = new System.Drawing.Size(260, 260);
            this.logoPictureBox.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.logoPictureBox.TabIndex = 0;
            this.logoPictureBox.TabStop = false;
            //
            // titleLabel
            //
            this.titleLabel.Font = new System.Drawing.Font("Segoe UI", 36F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point);
            this.titleLabel.ForeColor = System.Drawing.Color.Black;
            this.titleLabel.Location = new System.Drawing.Point(0, 295);
            this.titleLabel.Name = "titleLabel";
            this.titleLabel.Size = new System.Drawing.Size(480, 70);
            this.titleLabel.TabIndex = 1;
            this.titleLabel.Text = "VIGIL";
            this.titleLabel.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            //
            // spinner
            //
            this.spinner.ForeColor = System.Drawing.Color.Black;
            this.spinner.Location = new System.Drawing.Point(220, 385);
            this.spinner.Name = "spinner";
            this.spinner.Size = new System.Drawing.Size(40, 40);
            this.spinner.TabIndex = 2;
            //
            // taglineLabel
            //
            this.taglineLabel.Font = new System.Drawing.Font("Segoe UI", 11F, System.Drawing.FontStyle.Italic, System.Drawing.GraphicsUnit.Point);
            this.taglineLabel.ForeColor = System.Drawing.Color.DimGray;
            this.taglineLabel.Location = new System.Drawing.Point(0, 445);
            this.taglineLabel.Name = "taglineLabel";
            this.taglineLabel.Size = new System.Drawing.Size(480, 30);
            this.taglineLabel.TabIndex = 3;
            this.taglineLabel.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            //
            // taglineTimer
            //
            this.taglineTimer.Interval = 2000;
            this.taglineTimer.Tick += new System.EventHandler(this.TaglineTimer_Tick);
            //
            // fadeTimer
            //
            this.fadeTimer.Interval = 30;
            this.fadeTimer.Tick += new System.EventHandler(this.FadeTimer_Tick);
            //
            // LoaderForm
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.White;
            this.ClientSize = new System.Drawing.Size(480, 510);
            this.Controls.Add(this.taglineLabel);
            this.Controls.Add(this.spinner);
            this.Controls.Add(this.titleLabel);
            this.Controls.Add(this.logoPictureBox);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            this.Name = "LoaderForm";
            this.ShowInTaskbar = true;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "VIGIL";
            ((System.ComponentModel.ISupportInitialize)(this.logoPictureBox)).EndInit();
            this.ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.PictureBox logoPictureBox;
        private System.Windows.Forms.Label titleLabel;
        private VIGIL.Controls.SpinnerControl spinner;
        private System.Windows.Forms.Label taglineLabel;
        private System.Windows.Forms.Timer taglineTimer;
        private System.Windows.Forms.Timer fadeTimer;
    }
}
