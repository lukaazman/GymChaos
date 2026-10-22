using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>One frame of kart control, shared by the player and the AI.</summary>
public struct GymRaceDriveInput
{
    public float Throttle;
    public float Brake;
    public float Steer;
    public bool DriftHeld;
    public bool BoostPressed;
    public bool LookBack;
    public bool RespawnPressed;
    public bool HornPressed;
}

/// <summary>
/// Player race controls (Mario Kart style):
/// W / Up / RT accelerate, S / Down / LT brake and reverse, A-D / arrows /
/// left stick steer, Space / right mouse / RB drift, Left Shift / left mouse /
/// gamepad A boost, Q / LB look back, R / Y respawn, H horn. Esc stays pause.
/// </summary>
public static class GymRaceInput
{
    public static GymRaceDriveInput Read()
    {
        GymRaceDriveInput input = new GymRaceDriveInput();
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;
        Gamepad pad = Gamepad.current;
        if (keyboard != null)
        {
            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) input.Throttle = 1f;
            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) input.Brake = 1f;
            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) input.Steer -= 1f;
            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) input.Steer += 1f;
            input.DriftHeld |= keyboard.spaceKey.isPressed;
            input.BoostPressed |= keyboard.leftShiftKey.wasPressedThisFrame;
            input.LookBack |= keyboard.qKey.isPressed;
            input.RespawnPressed |= keyboard.rKey.wasPressedThisFrame;
            input.HornPressed |= keyboard.hKey.wasPressedThisFrame;
        }
        if (mouse != null)
        {
            input.DriftHeld |= mouse.rightButton.isPressed;
            input.BoostPressed |= mouse.leftButton.wasPressedThisFrame;
        }
        if (pad != null)
        {
            input.Throttle = Mathf.Max(input.Throttle, pad.rightTrigger.ReadValue());
            input.Brake = Mathf.Max(input.Brake, pad.leftTrigger.ReadValue());
            float stick = pad.leftStick.ReadValue().x;
            if (Mathf.Abs(stick) > 0.12f)
            {
                input.Steer += stick;
            }
            input.DriftHeld |= pad.rightShoulder.isPressed;
            input.BoostPressed |= pad.buttonSouth.wasPressedThisFrame;
            input.LookBack |= pad.leftShoulder.isPressed;
            input.RespawnPressed |= pad.buttonNorth.wasPressedThisFrame;
            input.HornPressed |= pad.leftStickButton.wasPressedThisFrame;
        }
#else
        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) input.Throttle = 1f;
        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) input.Brake = 1f;
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) input.Steer -= 1f;
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) input.Steer += 1f;
        input.DriftHeld = Input.GetKey(KeyCode.Space) || Input.GetMouseButton(1);
        input.BoostPressed = Input.GetKeyDown(KeyCode.LeftShift) || Input.GetMouseButtonDown(0);
        input.LookBack = Input.GetKey(KeyCode.Q);
        input.RespawnPressed = Input.GetKeyDown(KeyCode.R);
        input.HornPressed = Input.GetKeyDown(KeyCode.H);
#endif
        input.Steer = Mathf.Clamp(input.Steer, -1f, 1f);
        return input;
    }
}
