using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float rotationSpeed = 1.5f;

    [Header("References")]
    [SerializeField] private Joystick joystick;
    [SerializeField] private PlayerAnimationController animationController;
    [SerializeField] private WeaponController weaponController;

    private CharacterController characterController;
    private Vector3 verticalVelocity;
    private Camera mainCamera;

    private const float Gravity = -20f;

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
        mainCamera = Camera.main;

        if (animationController == null)
        {
            animationController = GetComponent<PlayerAnimationController>();
        }

        if (weaponController == null)
        {
            weaponController = GetComponent<WeaponController>();
        }
    }

    private void Update()
    {
        Move();
    }

    private void Move()
    {
        bool movementLocked = weaponController != null &&
            (weaponController.IsActionLocked || weaponController.IsReloading);

        float horizontal = movementLocked ? 0f : (joystick != null ? joystick.Horizontal : Input.GetAxisRaw("Horizontal"));
        float vertical = movementLocked ? 0f : (joystick != null ? joystick.Vertical : Input.GetAxisRaw("Vertical"));

        Vector3 moveDirection = new Vector3(horizontal, 0f, vertical);
        moveDirection = Vector3.ClampMagnitude(moveDirection, 1f);

        if (mainCamera != null && moveDirection.sqrMagnitude > 0.0001f)
        {
            Vector3 camForward = mainCamera.transform.forward;
            camForward.y = 0f;
            camForward.Normalize();

            Vector3 camRight = mainCamera.transform.right;
            camRight.y = 0f;
            camRight.Normalize();

            moveDirection = Vector3.ClampMagnitude(camForward * vertical + camRight * horizontal, 1f);
        }

        characterController.Move(moveDirection * moveSpeed * Time.deltaTime);

        if (moveDirection.sqrMagnitude > 0.01f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(moveDirection);
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                targetRotation,
                rotationSpeed * Time.deltaTime
            );

            weaponController?.ResetToIdlePose();
        }

        animationController?.SetMoveSpeed(moveDirection.magnitude, moveDirection.magnitude * moveSpeed);

        if (characterController.isGrounded && verticalVelocity.y < 0)
        {
            verticalVelocity.y = -2f;
        }

        verticalVelocity.y += Gravity * Time.deltaTime;
        characterController.Move(verticalVelocity * Time.deltaTime);
    }
}