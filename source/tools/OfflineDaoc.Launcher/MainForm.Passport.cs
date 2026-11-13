// Character passport (docs/CHARACTER-PASSPORT.md): the launcher's entry to the passport window. Kept apart from
// MainForm.cs so the hosting work can change that file without touching this one.
namespace OfflineDaoc.Launcher;

internal sealed partial class MainForm
{
    private PassportForm? _passportForm;

    private Button PassportButton()
    {
        Button button = ActionButton("CHARACTER PASSPORT…", DaocTheme.Gold);
        button.Dock = DockStyle.Fill;
        button.Margin = new Padding(0);
        _helpTip.SetToolTip(button, "Take one of your characters to a friend's world and bring it home again.");
        button.Click += (_, _) =>
        {
            if (_passportForm == null || _passportForm.IsDisposed)
            {
                _passportForm = new PassportForm(_root, PortableCredentials.ReadOrCreate(_root).Account,
                    () => IsServerRunning() || FindExactServerProcess() is not null);
                _passportForm.Show(this);
            }
            else
            {
                _passportForm.WindowState = FormWindowState.Normal;
                _passportForm.Activate();
            }
        };
        return button;
    }
}
