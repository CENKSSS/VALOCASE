using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using ValoCase.UI;

namespace ValoCase.Core
{
    public sealed class GameBootstrap : MonoBehaviour
    {
        [SerializeField] LoadingScreenController loadingScreen;
        [SerializeField] float minimumLoadSeconds = 1.2f;
        [SerializeField] string mainSceneName = SceneNames.Main;

        IEnumerator Start()
        {
            loadingScreen?.Show(0f);

            var elapsed = 0f;
            while (elapsed < minimumLoadSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                loadingScreen?.Show(Mathf.Clamp01(elapsed / minimumLoadSeconds));
                yield return null;
            }

            if (GameContext.Instance == null)
            {
                Debug.LogError("[Bootstrap] GameContext missing. Add GameContext prefab to Bootstrap scene.");
                yield break;
            }

            loadingScreen?.Show(1f);
            yield return SceneManager.LoadSceneAsync(mainSceneName);
            loadingScreen?.Hide();
        }
    }
}
