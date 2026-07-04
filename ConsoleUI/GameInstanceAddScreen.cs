using System;
using System.IO;

using CKAN.ConsoleUI.Toolkit;

namespace CKAN.ConsoleUI {

    /// <summary>
    /// Screen for adding a new game instance
    /// </summary>
    public class GameInstanceAddScreen : GameInstanceScreen {

        /// <summary>
        /// Initialize the Screen
        /// </summary>
        /// <param name="theme">The visual theme to use to draw the dialog</param>
        /// <param name="mgr">Game instance manager containing the instances</param>
        public GameInstanceAddScreen(ConsoleTheme theme, GameInstanceManager mgr)
            : base(theme, mgr)
        {
            AddObject(new ConsoleLabel(
                labelWidth, pathRow + 1, -1,
                () => string.Format(Properties.Resources.InstanceAddExample, examplePath),
                null, th => th.DimLabelFg
            ));
        }

        /// <summary>
        /// Return whether the fields are valid.
        /// The basic non-empty and unique checks are good enough for adding.
        /// </summary>
        protected override bool Valid()
            => nameValid() && pathValid();

        /// <summary>
        /// Put description in top center
        /// </summary>
        protected override string CenterHeader()
            => Properties.Resources.InstanceAddTitle;

        /// <summary>
        /// Add the instance
        /// </summary>
        /// <returns>true if the instance was added, false if the user declined a confirmation or cancelled the game selection (keeps the screen open)</returns>
        protected override bool Save()
            // Pass ourselves as the IUser so the shared mod folder confirmation
            // surfaces as a dialog in the ConsoleUI. null means the user declined
            // that confirmation or cancelled the game selection dialog; either
            // way nothing was registered, so the screen must not close as if the
            // instance had been added.
            => manager.AddInstance(path.Value, name.Value, this) != null;

        private static readonly string examplePath = Path.Combine(
            !string.IsNullOrEmpty(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles))
                ? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
                : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Kerbal Space Program"
        );
    }

}
