using UnityEngine;
using System.Collections;
using UnityEngine.InputSystem;

public class VariousMouseOrbit : MonoBehaviour
{

    Transform Target;
    public Transform[] Targets;
    int i = 0;
    public float distance;

    public float xSpeed = 250.0f;
    public float ySpeed = 120.0f;

    public float yMinLimit = -20.0f;
    public float yMaxLimit = 80.0f;

    private float x = 0.0f;
    private float y = 0.0f;
    public float CameraDist = 10;

    // Use this for initialization
    void Start()
    {
        Vector3 angles = transform.eulerAngles;
        x = angles.x+50;
        y = angles.y;
        distance = 30;
        Target = Targets[0];
        if (this.GetComponent<Rigidbody>() == true)
            GetComponent<Rigidbody>().freezeRotation = true;
    }

    // Update is called once per frame
    void LateUpdate()
    {
        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;

        if(keyboard != null && keyboard.vKey.wasPressedThisFrame)
        {
            if (i < Targets.Length-1)
                i++;
            else if (i >= Targets.Length-1)
                i = 0;
            Target = Targets[i];
        }

        if (mouse != null && mouse.rightButton.isPressed)
        {
            if (Target)
            {
                Vector2 mouseDelta = mouse.delta.ReadValue();
                x += mouseDelta.x * xSpeed * 0.02f;
                y += mouseDelta.y * ySpeed * 0.05f;

                y = ClampAngle(y, yMinLimit, yMaxLimit);

                Quaternion rotation = Quaternion.Euler(y, x, 0);
                Vector3 position = rotation * new Vector3(0, 0, -distance) + Target.position;

                transform.rotation = rotation;
                transform.position = position;
                distance = CameraDist;

                if (keyboard != null && keyboard.wKey.isPressed)
                {
                    CameraDist -= Time.deltaTime * 20f;
                    CameraDist = Mathf.Clamp(CameraDist,2,80);
                }
                if (keyboard != null && keyboard.sKey.isPressed)
                {
                    CameraDist += Time.deltaTime * 20f;
                    CameraDist = Mathf.Clamp(CameraDist, 2, 80);
                }
            }
        }
    }

    float ClampAngle(float ag, float min, float max)
    {
        if (ag < -360)
            ag += 360;
        if (ag > 360)
            ag -= 360;
        return Mathf.Clamp(ag, min, max);
    }
}
