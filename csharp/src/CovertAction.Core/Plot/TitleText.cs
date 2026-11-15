namespace CovertAction.Core.Final;

/// <summary>The texts of the title screen, the main menu and the creation of the agent, in the order of the program's data.</summary>
public enum TitleText
{
    /// <summary>"Move JOYSTICK to upper-left corner and press button.".</summary>
    JoystickUpperLeft,
    /// <summary>"Move JOYSTICK to lower-right corner and press button.".</summary>
    JoystickLowerRight,
    /// <summary>The name of the picture shown before the title ("labs.pic").</summary>
    LabsPicture,
    /// <summary>The name of the title animation ("title2.pan").</summary>
    TitleAnimation,
    /// <summary>The name of the credits animation ("credits.pan").</summary>
    CreditsAnimation,
    /// <summary>The main menu: "Do you want to..." and its four choices.</summary>
    MainMenu,
    /// <summary>The name of the picture of the two agents ("gender.pic").</summary>
    GenderPicture,
    /// <summary>"Select one..." and the two agents' names.</summary>
    GenderMenu,
    /// <summary>"Which difficulty level?" and its four choices (the copy the new agent's screens use).</summary>
    LevelMenu,
    /// <summary>Number of texts.</summary>
    Count,
}
