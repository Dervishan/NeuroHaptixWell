using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneSwitcher : MonoBehaviour
{

    [Header("Toggle")]
    [Tooltip("If checked, uses the Alternate Scenes")]
    public bool useAlternateScenes;

    [Header("Default Scenes")]
    public string defaultSceneA = "Circle";
    public string defaultSceneB = "Triangle";
    public string defaultSceneC = "Star";

    [Header("Alternate Scenes")]
    public string alternateSceneA = "CircleHaptic";
    public string alternateSceneB = "TriangleHaptic";
    public string alternateSceneC = "StarHaptic";

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.A))
        {
            SceneManager.LoadScene(useAlternateScenes ? alternateSceneA : defaultSceneA);
        }

        if (Input.GetKeyDown(KeyCode.B))
        {
            SceneManager.LoadScene(useAlternateScenes ? alternateSceneB : defaultSceneB);
        }

        if (Input.GetKeyDown(KeyCode.C))
        {
            SceneManager.LoadScene(useAlternateScenes ? alternateSceneC : defaultSceneC);
        }
    }
}
