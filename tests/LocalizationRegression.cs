namespace SyncPlayer;

static class LocalizationRegression
{
    public static void Run()
    {
        var report = new List<string>(); string previous = Localization.Language;
        try {
            string settings = Path.GetFullPath("diagnostics/language-settings-test.json");
            DeviceIdentity.SaveName("My device", settings);
            DeviceIdentity.SaveSetting("language", "zh-Hant", settings);
            if (DeviceIdentity.LoadName(settings) != "My device" || DeviceIdentity.LoadSetting("language", settings) != "zh-Hant") throw new Exception("Settings persistence lost name or language");
            DeviceIdentity.SaveName("Renamed", settings);
            if (DeviceIdentity.LoadSetting("language", settings) != "zh-Hant") throw new Exception("Rename lost language");
            report.Add("PASS language/name settings persist without overwriting each other");
            using var form = new MainForm(false);
            Control[] Descendants(Control root) => root.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Descendants(c))).ToArray();
            var controls = Descendants(form); var grid = controls.OfType<DataGridView>().Single();
            var master = controls.OfType<ComboBox>().First(c => c.Dock == DockStyle.Fill);
            var selected = master.SelectedItem;
            foreach (string language in new[] { "en", "zh-Hans", "zh-Hant", "en" }) {
                form.TestLanguage(language);
                string expected = language == "en" ? "Follow" : language == "zh-Hans" ? "跟随" : "跟隨";
                if (grid.Columns[0].HeaderText != expected) throw new Exception("Header translation failed: " + language);
                if (!Equals(selected, master.SelectedItem)) throw new Exception("Language change lost selection");
                if (!File.Exists(Localization.Manual)) throw new Exception("Missing manual: " + Localization.Manual);
                if (grid.ColumnHeadersDefaultCellStyle.WrapMode != DataGridViewTriState.False) throw new Exception("Wrapping headers");
                report.Add("PASS " + language + ": translated controls, preserved source, matching manual, single-line headers");
            }
            var linked = controls.OfType<ToggleSwitch>().First();
            var remoteSwitch = controls.OfType<ToggleSwitch>().Last();
            if (!linked.Checked || remoteSwitch.Checked) throw new Exception("Default sync switches");
            linked.Checked = false;
            if (grid.Rows.Cast<DataGridViewRow>().Count(r => !r.ReadOnly) != (selected == null ? 0 : 1)) throw new Exception("Other local windows remain visible with local sync off");
            linked.Checked = true;
            if (controls.OfType<IconButton>().Count() != 4 || controls.OfType<IconButton>().Any(b => b.Width != b.Height || b.Text.Length != 0)) throw new Exception("Icon button layout");
            report.Add("PASS default switches, local-off hides followers, four square icon-only buttons");
            form.Show();
            var editable = grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => !r.ReadOnly && !r.Cells[2].ReadOnly);
            if (editable != null) {
                grid.CurrentCell = editable.Cells[2];
                if (!grid.BeginEdit(false) || grid.EditingControl is not TextBox editor) throw new Exception("Offset editor unavailable");
                editor.Text = "invalid";
                form.TestLanguage("zh-Hans");
                if (Localization.Language != "zh-Hans" || grid.Columns[0].HeaderText != "跟随" || editor.Text != "invalid") throw new Exception("Invalid offset blocked language selection or lost input");
                grid.CancelEdit();
                report.Add("PASS invalid offset does not block language selection; unfinished input retained");
            }
            File.WriteAllLines("language-test-result.txt", report);
        } finally { Localization.Select(previous, false); }
    }
}
