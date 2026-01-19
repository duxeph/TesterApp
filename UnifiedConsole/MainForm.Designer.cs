namespace UnifiedConsole
{
    partial class MainForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        // Designer-created controls
        private System.Windows.Forms.Panel topPanel;
        private System.Windows.Forms.Label lblBitParser;
        private System.Windows.Forms.Button btnLoadSchema;
        private System.Windows.Forms.Button _btnStart;
        private System.Windows.Forms.Button _btnStop;
        private System.Windows.Forms.Label _lblStatus;
        private System.Windows.Forms.SplitContainer mainSplit;
        private System.Windows.Forms.TabControl _tabTests;
        private System.Windows.Forms.SplitContainer rightSplit;
        private System.Windows.Forms.Panel logPanel;
        private System.Windows.Forms.Label lblLog;
        private System.Windows.Forms.TextBox _txtLog;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            
            // Cleanup
            StopBitParser();
            _perfRunning = false;
            _perfThread?.Join(500);
            
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.topPanel = new System.Windows.Forms.Panel();
            this.lblBitParser = new System.Windows.Forms.Label();
            this.btnLoadSchema = new System.Windows.Forms.Button();
            this._btnStart = new System.Windows.Forms.Button();
            this._btnStop = new System.Windows.Forms.Button();
            this._lblStatus = new System.Windows.Forms.Label();
            this.mainSplit = new System.Windows.Forms.SplitContainer();
            this._tabTests = new System.Windows.Forms.TabControl();
            this.rightSplit = new System.Windows.Forms.SplitContainer();
            this.logPanel = new System.Windows.Forms.Panel();
            this.lblLog = new System.Windows.Forms.Label();
            this._txtLog = new System.Windows.Forms.TextBox();
            ((System.ComponentModel.ISupportInitialize)(this.mainSplit)).BeginInit();
            this.mainSplit.Panel1.SuspendLayout();
            this.mainSplit.Panel2.SuspendLayout();
            this.mainSplit.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.rightSplit)).BeginInit();
            this.rightSplit.Panel2.SuspendLayout();
            this.rightSplit.SuspendLayout();
            this.logPanel.SuspendLayout();
            this.SuspendLayout();
            // 
            // topPanel
            // 
            this.topPanel.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(235)))), ((int)(((byte)(240)))), ((int)(((byte)(250)))));
            this.topPanel.Controls.Add(this._lblStatus);
            this.topPanel.Controls.Add(this._btnStop);
            this.topPanel.Controls.Add(this._btnStart);
            this.topPanel.Controls.Add(this.btnLoadSchema);
            this.topPanel.Controls.Add(this.lblBitParser);
            this.topPanel.Dock = System.Windows.Forms.DockStyle.Top;
            this.topPanel.Location = new System.Drawing.Point(0, 0);
            this.topPanel.Name = "topPanel";
            this.topPanel.Padding = new System.Windows.Forms.Padding(10);
            this.topPanel.Size = new System.Drawing.Size(1400, 65);
            this.topPanel.TabIndex = 0;
            // 
            // lblBitParser
            // 
            this.lblBitParser.AutoSize = true;
            this.lblBitParser.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
            this.lblBitParser.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(50)))), ((int)(((byte)(50)))), ((int)(((byte)(80)))));
            this.lblBitParser.Location = new System.Drawing.Point(10, 23);
            this.lblBitParser.Name = "lblBitParser";
            this.lblBitParser.Size = new System.Drawing.Size(88, 19);
            this.lblBitParser.TabIndex = 0;
            this.lblBitParser.Text = "📊 Bit Parser:";
            // 
            // btnLoadSchema
            // 
            this.btnLoadSchema.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(70)))), ((int)(((byte)(130)))), ((int)(((byte)(180)))));
            this.btnLoadSchema.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnLoadSchema.ForeColor = System.Drawing.Color.White;
            this.btnLoadSchema.Location = new System.Drawing.Point(110, 16);
            this.btnLoadSchema.Name = "btnLoadSchema";
            this.btnLoadSchema.Size = new System.Drawing.Size(120, 32);
            this.btnLoadSchema.TabIndex = 1;
            this.btnLoadSchema.Text = "📂 Load Schema";
            this.btnLoadSchema.UseVisualStyleBackColor = false;
            this.btnLoadSchema.Click += new System.EventHandler(this.BtnLoadSchema_Click);
            // 
            // _btnStart
            // 
            this._btnStart.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(60)))), ((int)(((byte)(160)))), ((int)(((byte)(60)))));
            this._btnStart.Enabled = false;
            this._btnStart.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this._btnStart.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
            this._btnStart.ForeColor = System.Drawing.Color.White;
            this._btnStart.Location = new System.Drawing.Point(240, 16);
            this._btnStart.Name = "_btnStart";
            this._btnStart.Size = new System.Drawing.Size(90, 32);
            this._btnStart.TabIndex = 2;
            this._btnStart.Text = "▶ START";
            this._btnStart.UseVisualStyleBackColor = false;
            this._btnStart.Click += new System.EventHandler(this.BtnStartBitParser_Click);
            // 
            // _btnStop
            // 
            this._btnStop.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(180)))), ((int)(((byte)(60)))), ((int)(((byte)(60)))));
            this._btnStop.Enabled = false;
            this._btnStop.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this._btnStop.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
            this._btnStop.ForeColor = System.Drawing.Color.White;
            this._btnStop.Location = new System.Drawing.Point(340, 16);
            this._btnStop.Name = "_btnStop";
            this._btnStop.Size = new System.Drawing.Size(90, 32);
            this._btnStop.TabIndex = 3;
            this._btnStop.Text = "■ STOP";
            this._btnStop.UseVisualStyleBackColor = false;
            this._btnStop.Click += new System.EventHandler(this.BtnStopBitParser_Click);
            // 
            // _lblStatus
            // 
            this._lblStatus.Font = new System.Drawing.Font("Consolas", 10F);
            this._lblStatus.ForeColor = System.Drawing.Color.Gray;
            this._lblStatus.Location = new System.Drawing.Point(450, 22);
            this._lblStatus.Name = "_lblStatus";
            this._lblStatus.Size = new System.Drawing.Size(500, 25);
            this._lblStatus.TabIndex = 4;
            this._lblStatus.Text = "Status: Load schema to start";
            // 
            // mainSplit
            // 
            this.mainSplit.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(220)))), ((int)(((byte)(225)))), ((int)(((byte)(235)))));
            this.mainSplit.Dock = System.Windows.Forms.DockStyle.Fill;
            this.mainSplit.FixedPanel = System.Windows.Forms.FixedPanel.Panel1;
            this.mainSplit.Location = new System.Drawing.Point(0, 65);
            this.mainSplit.Name = "mainSplit";
            this.mainSplit.Orientation = System.Windows.Forms.Orientation.Vertical;
            // 
            // mainSplit.Panel1
            // 
            this.mainSplit.Panel1.Controls.Add(this._tabTests);
            // 
            // mainSplit.Panel2
            // 
            this.mainSplit.Panel2.Controls.Add(this.rightSplit);
            this.mainSplit.Size = new System.Drawing.Size(1400, 885);
            this.mainSplit.SplitterDistance = 480;
            this.mainSplit.TabIndex = 1;
            // 
            // _tabTests
            // 
            this._tabTests.Dock = System.Windows.Forms.DockStyle.Fill;
            this._tabTests.Font = new System.Drawing.Font("Segoe UI", 10F);
            this._tabTests.Location = new System.Drawing.Point(0, 0);
            this._tabTests.Name = "_tabTests";
            this._tabTests.Padding = new System.Drawing.Point(12, 5);
            this._tabTests.SelectedIndex = 0;
            this._tabTests.Size = new System.Drawing.Size(480, 885);
            this._tabTests.TabIndex = 0;
            // 
            // rightSplit
            // 
            this.rightSplit.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(230)))), ((int)(((byte)(235)))), ((int)(((byte)(245)))));
            this.rightSplit.Dock = System.Windows.Forms.DockStyle.Fill;
            this.rightSplit.Location = new System.Drawing.Point(0, 0);
            this.rightSplit.Name = "rightSplit";
            this.rightSplit.Orientation = System.Windows.Forms.Orientation.Horizontal;
            // 
            // rightSplit.Panel2
            // 
            this.rightSplit.Panel2.Controls.Add(this.logPanel);
            this.rightSplit.Size = new System.Drawing.Size(916, 885);
            this.rightSplit.SplitterDistance = 705;
            this.rightSplit.TabIndex = 0;
            // 
            // logPanel
            // 
            this.logPanel.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(250)))), ((int)(((byte)(252)))), ((int)(((byte)(255)))));
            this.logPanel.Controls.Add(this._txtLog);
            this.logPanel.Controls.Add(this.lblLog);
            this.logPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.logPanel.Location = new System.Drawing.Point(0, 0);
            this.logPanel.Name = "logPanel";
            this.logPanel.Padding = new System.Windows.Forms.Padding(5);
            this.logPanel.Size = new System.Drawing.Size(916, 176);
            this.logPanel.TabIndex = 0;
            // 
            // lblLog
            // 
            this.lblLog.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(235)))), ((int)(((byte)(240)))), ((int)(((byte)(248)))));
            this.lblLog.Dock = System.Windows.Forms.DockStyle.Top;
            this.lblLog.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
            this.lblLog.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(60)))), ((int)(((byte)(60)))), ((int)(((byte)(80)))));
            this.lblLog.Location = new System.Drawing.Point(5, 5);
            this.lblLog.Name = "lblLog";
            this.lblLog.Size = new System.Drawing.Size(906, 22);
            this.lblLog.TabIndex = 0;
            this.lblLog.Text = "📋 Log";
            // 
            // _txtLog
            // 
            this._txtLog.BackColor = System.Drawing.Color.White;
            this._txtLog.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this._txtLog.Dock = System.Windows.Forms.DockStyle.Fill;
            this._txtLog.Font = new System.Drawing.Font("Consolas", 8F);
            this._txtLog.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(40)))), ((int)(((byte)(80)))), ((int)(((byte)(40)))));
            this._txtLog.Location = new System.Drawing.Point(5, 27);
            this._txtLog.Multiline = true;
            this._txtLog.Name = "_txtLog";
            this._txtLog.ReadOnly = true;
            this._txtLog.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this._txtLog.Size = new System.Drawing.Size(906, 144);
            this._txtLog.TabIndex = 1;
            // 
            // MainForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(245)))), ((int)(((byte)(248)))), ((int)(((byte)(252)))));
            this.ClientSize = new System.Drawing.Size(1400, 950);
            this.Controls.Add(this.mainSplit);
            this.Controls.Add(this.topPanel);
            this.DoubleBuffered = true;
            this.Name = "MainForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "🔧 Unified Test Console - Simplified";
            this.Load += new System.EventHandler(this.MainForm_Load);
            this.mainSplit.Panel1.ResumeLayout(false);
            this.mainSplit.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.mainSplit)).EndInit();
            this.mainSplit.ResumeLayout(false);
            this.rightSplit.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.rightSplit)).EndInit();
            this.rightSplit.ResumeLayout(false);
            this.logPanel.ResumeLayout(false);
            this.logPanel.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion
    }
}
