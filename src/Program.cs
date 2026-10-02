using System;
using System.Windows.Forms;

namespace Sm0kiSoloCoach
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();

            try
            {
                var profile = UserProfileStore.Load();
                if (!profile.SetupComplete)
                {
                    var prefs = UserSettingsStore.Load();
                    using var setup = new FirstRunSetupForm(profile, prefs, true);
                    if (setup.ShowDialog() != DialogResult.OK)
                        return;
                }

                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Sm0ki Solo Coach se ni mogel zagnati.\n\n" +
                    ex.GetType().Name + ":\n" + ex.Message + "\n\n" +
                    "Pošlji screenshot tega okna v ChatGPT.",
                    "Sm0ki Solo Coach - Startup Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
        }
    }
}
