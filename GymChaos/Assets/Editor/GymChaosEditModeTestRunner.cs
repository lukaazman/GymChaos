#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

/// <summary>
/// Batch entry for the CI EditMode tests. game-ci/unity-builder activates the
/// personal license reliably (it randomizes the machine id), the test runner
/// action does not, so CI runs the tests through this method instead.
/// </summary>
public static class GymChaosEditModeTestRunner
{
    public static void Run()
    {
        TestRunnerApi api = ScriptableObject.CreateInstance<TestRunnerApi>();
        api.RegisterCallbacks(new Callbacks());
        api.Execute(new ExecutionSettings(new Filter { testMode = TestMode.EditMode }));
    }

    private sealed class Callbacks : ICallbacks
    {
        private int failed;

        public void RunStarted(ITestAdaptor testsToRun) { }

        public void TestStarted(ITestAdaptor test) { }

        public void TestFinished(ITestResultAdaptor result)
        {
            if (!result.HasChildren && result.TestStatus == TestStatus.Failed)
            {
                failed++;
                Debug.LogError($"GYMCHAOS_EDITMODE_TEST_FAILED {result.FullName}: {result.Message}");
            }
        }

        public void RunFinished(ITestResultAdaptor result)
        {
            Debug.Log(failed == 0
                ? $"GYMCHAOS_EDITMODE_TESTS_OK passed={result.PassCount}"
                : $"GYMCHAOS_EDITMODE_TESTS_FAILED failed={failed}");
            EditorApplication.Exit(failed == 0 ? 0 : 1);
        }
    }
}
#endif
