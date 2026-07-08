using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float rotationSpeed = 12f;

    [Header("References")]
    [SerializeField] private Joystick joystick;
    [SerializeField] private PlayerAnimationController animationController;

    private CharacterController characterController;
    private Vector3 verticalVelocity;

    private const float Gravity = -20f;

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();

        if (animationController == null)
        {
            animationController = GetComponent<PlayerAnimationController>();
        }
    }

    private void Update()
    {
        Move();
    }

    private void Move()
    {
        float horizontal = joystick != null ? joystick.Horizontal : Input.GetAxisRaw("Horizontal");
        float vertical = joystick != null ? joystick.Vertical : Input.GetAxisRaw("Vertical");

        Vector3 moveDirection = new Vector3(horizontal, 0f, vertical);
        moveDirection = Vector3.ClampMagnitude(moveDirection, 1f);

        characterController.Move(moveDirection * moveSpeed * Time.deltaTime);

        if (moveDirection.sqrMagnitude > 0.01f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(moveDirection);
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                targetRotation,
                rotationSpeed * Time.deltaTime
            );
        }

        animationController?.SetMoveSpeed(moveDirection.magnitude);

        if (characterController.isGrounded && verticalVelocity.y < 0)
        {
            verticalVelocity.y = -2f;
        }

        verticalVelocity.y += Gravity * Time.deltaTime;
        characterController.Move(verticalVelocity * Time.deltaTime);
    }
}