// A small "type a name" window.
using System.Drawing;
using System.Windows.Forms;

namespace SkinManager.App
{
    class InputForm : Form
    {
        readonly TextBox _box = new TextBox { Width = 360, Font = Theme.Body, BorderStyle = BorderStyle.FixedSingle };
        public string Value => _box.Text.Trim();

        public InputForm(string title, string prompt, string value, string ok, string extraOption = null)
        {
            Text = title; BackColor = Theme.Back; ForeColor = Theme.Text; Font = Theme.Body;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent; AutoScaleMode = AutoScaleMode.Dpi; ClientSize = new Size(400, extraOption == null ? 150 : 180);
            var label = new Label { Text = prompt, AutoSize = false, Location = new Point(20, 16), Size = new Size(360, 36), ForeColor = Theme.Text, BackColor = Color.Transparent };
            _box.Location = new Point(20, 56); _box.Text = value ?? ""; _box.BackColor = Theme.Panel; _box.ForeColor = Theme.Text;
            Controls.Add(label); Controls.Add(_box);
            if (extraOption != null)
            {
                Option = new CheckBox { Text = extraOption, AutoSize = true, Location = new Point(20, 90), ForeColor = Theme.Text };
                Controls.Add(Option);
            }
            var okButton = Theme.Primary(ok); var cancel = Theme.Secondary("Cancel");
            okButton.DialogResult = DialogResult.OK; cancel.DialogResult = DialogResult.Cancel;
            AcceptButton = okButton; CancelButton = cancel;
            Controls.Add(okButton); Controls.Add(cancel);
            Load += (s, e) =>
            {
                okButton.Location = new Point(ClientSize.Width - okButton.Width - 20, ClientSize.Height - okButton.Height - 16);
                cancel.Location = new Point(okButton.Left - cancel.Width - 8, okButton.Top);
                _box.SelectAll(); _box.Focus();
            };
        }

        public CheckBox Option { get; }
    }
}
