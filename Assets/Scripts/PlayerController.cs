using UnityEngine;

public class PlayerController : MonoBehaviour
{
    public float walkSpeed = 5f;
    public float runSpeed = 8f;
    public float jumpHeight = 1.5f;
    public float gravity = -20f;
    public float mouseSensitivity = 2f;

    [Header("Mobile")]
    public bool forceMobileControlsInEditor = false;
    public float touchLookSensitivity = 0.12f;

    private CharacterController controller;
    private Camera playerCamera;
    private float verticalVelocity;
    private float cameraRotationX;
    private MobileTouchControls touchControls;
    private bool mobile;
    [Header("Creative Flight (Desktop)")]
    public bool enableCreativeFlight = true;
    public float doubleSpaceInterval = 0.3f;
    [Min(0.1f)] public float flySpeed = 12f;
    [Min(0.1f)] public float fastFlySpeed = 30f;
    private bool flying;
    private float lastSpaceTime = -100f;

    private void Start()
    {
        controller = GetComponent<CharacterController>();
        if (controller == null) controller = gameObject.AddComponent<CharacterController>();
        controller.height = 2f;
        controller.radius = 0.4f;
        controller.center = new Vector3(0f, 1f, 0f);

        playerCamera = GetComponentInChildren<Camera>();
        if (playerCamera == null)
        {
            var cameraObject = new GameObject("PlayerCamera");
            cameraObject.transform.SetParent(transform, false);
            cameraObject.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            playerCamera = cameraObject.AddComponent<Camera>();
            playerCamera.fieldOfView = 75f;
        }

        mobile = Application.isMobilePlatform || forceMobileControlsInEditor;
        if (mobile)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            touchControls = MobileTouchControls.Create();
        }
        else
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        Debug.Log("PlayerController initialized. Mobile controls: " + mobile);
    }

    private void Update()
    {
        if (playerCamera == null) return;

        float lookX, lookY;
        Vector2 move;
        bool running, jump;
        if (mobile)
        {
            Vector2 delta = touchControls != null ? touchControls.ConsumeLookDelta() : Vector2.zero;
            lookX = delta.x * touchLookSensitivity;
            lookY = delta.y * touchLookSensitivity;
            move = touchControls != null ? touchControls.Move : Vector2.zero;
            running = touchControls != null && touchControls.Running;
            jump = touchControls != null && touchControls.ConsumeJump();
        }
        else
        {
            lookX = Input.GetAxis("Mouse X") * mouseSensitivity;
            lookY = Input.GetAxis("Mouse Y") * mouseSensitivity;
            move = new Vector2(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical"));
            running = Input.GetKey(KeyCode.LeftShift);
            jump = Input.GetKeyDown(KeyCode.Space);
            if (enableCreativeFlight && jump)
            {
                if (Time.unscaledTime - lastSpaceTime <= doubleSpaceInterval)
                {
                    flying = !flying;
                    verticalVelocity = 0f;
                    lastSpaceTime = -100f;
                    jump = false;
                }
                else lastSpaceTime = Time.unscaledTime;
            }
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            if (Input.GetMouseButtonDown(0))
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        transform.Rotate(Vector3.up * lookX);
        cameraRotationX = Mathf.Clamp(cameraRotationX - lookY, -85f, 85f);
        playerCamera.transform.localRotation = Quaternion.Euler(cameraRotationX, 0f, 0f);

        Vector3 movement = transform.right * move.x + transform.forward * move.y;
        movement = Vector3.ClampMagnitude(movement, 1f);
        if (flying && !mobile)
        {
            float vertical = (Input.GetKey(KeyCode.Space) ? 1f : 0f) -
                             (Input.GetKey(KeyCode.LeftShift) ? 1f : 0f);
            Vector3 flyDirection = Vector3.ClampMagnitude(movement + Vector3.up * vertical, 1f);
            float speed = Input.GetKey(KeyCode.LeftControl) ? fastFlySpeed : flySpeed;
            controller.Move(flyDirection * speed * Time.deltaTime);
            return;
        }
        if (controller.isGrounded)
        {
            if (verticalVelocity < 0f) verticalVelocity = -2f;
            if (jump) verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }
        verticalVelocity += gravity * Time.deltaTime;
        Vector3 velocity = movement * (running ? runSpeed : walkSpeed);
        velocity.y = verticalVelocity;
        controller.Move(velocity * Time.deltaTime);
    }
}
