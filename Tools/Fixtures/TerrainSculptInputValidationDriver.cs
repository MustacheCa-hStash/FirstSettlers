using System;
using UnityEngine;

// Installed only in the isolated validation project, after the production controller.
[DefaultExecutionOrder(250)]
public sealed class TerrainSculptInputValidationDriver:MonoBehaviour
{
    public static Action AfterFrame;
    private void LateUpdate()=>AfterFrame?.Invoke();
}
