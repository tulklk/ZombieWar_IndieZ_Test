/// <summary>
/// Hands the destination scene name across the MainMenu -> LoadingScene boundary
/// when MainMenuUIController is set to route level loads through LoadingScene.
/// LoadingSceneController reads and clears NextSceneName on Awake.
/// </summary>
public static class SceneLoadData
{
    public static string NextSceneName { get; set; }
}
