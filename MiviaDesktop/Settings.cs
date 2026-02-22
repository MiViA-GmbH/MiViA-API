using System.Collections.Generic;

namespace MiviaDesktop;

public class Settings
{
    public string? ServerUrl { get; set; }
    public string? AccessToken { get; set; }
    public string? InputDirectory { get; set; }

    public List<string>? SelectedModels { get; set; }

    /// <summary>
    /// Maps model internal name to selected customization ID.
    /// </summary>
    public Dictionary<string, string>? SelectedCustomizations { get; set; }
}