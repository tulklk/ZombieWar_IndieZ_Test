using UnityEngine;

[RequireComponent(typeof(Animator))]
public class WeaponHandIK : MonoBehaviour
{
    [SerializeField] private Animator animator;
    [SerializeField] private WeaponController weaponController;
    [SerializeField] private float ikBlendSpeed = 10f;
    [SerializeField] private bool useHandIK = false;

    private float currentLeftWeight;
    private float currentRightWeight;

    private void Awake()
    {
        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }

        if (weaponController == null)
        {
            weaponController = GetComponentInParent<WeaponController>();
        }
    }

    private void OnAnimatorIK(int layerIndex)
    {
        if (animator == null)
        {
            return;
        }

        bool isAiming = useHandIK && weaponController != null && weaponController.IsAiming;

        currentLeftWeight = ApplyHandIK(
            AvatarIKGoal.LeftHand,
            weaponController != null ? weaponController.CurrentLeftHandGrip : null,
            isAiming,
            currentLeftWeight);

        currentRightWeight = ApplyHandIK(
            AvatarIKGoal.RightHand,
            weaponController != null ? weaponController.CurrentRightHandGrip : null,
            isAiming,
            currentRightWeight);
    }

    private float ApplyHandIK(AvatarIKGoal goal, Transform grip, bool isAiming, float weight)
    {
        bool shouldHoldGrip = grip != null && isAiming;
        float targetWeight = shouldHoldGrip ? 1f : 0f;
        weight = Mathf.MoveTowards(weight, targetWeight, ikBlendSpeed * Time.deltaTime);

        animator.SetIKPositionWeight(goal, weight);
        animator.SetIKRotationWeight(goal, weight);

        if (weight > 0f && grip != null)
        {
            animator.SetIKPosition(goal, grip.position);
            animator.SetIKRotation(goal, grip.rotation);
        }

        return weight;
    }

    private void OnDrawGizmos()
    {
        bool isAiming = weaponController != null && weaponController.IsAiming;

        DrawGripGizmo(
            "L",
            weaponController != null ? weaponController.CurrentLeftHandGrip : null,
            animator != null ? animator.GetBoneTransform(HumanBodyBones.LeftHand) : null,
            currentLeftWeight,
            isAiming);

        DrawGripGizmo(
            "R",
            weaponController != null ? weaponController.CurrentRightHandGrip : null,
            animator != null ? animator.GetBoneTransform(HumanBodyBones.RightHand) : null,
            currentRightWeight,
            isAiming);
    }

    private static void DrawGripGizmo(string label, Transform grip, Transform handBone, float weight, bool isAiming)
    {
        if (grip == null)
        {
            return;
        }

        Gizmos.color = Color.magenta;
        Gizmos.DrawSphere(grip.position, 0.02f);

        Gizmos.color = Color.red;
        Gizmos.DrawLine(grip.position, grip.position + grip.right * 0.1f);
        Gizmos.color = Color.green;
        Gizmos.DrawLine(grip.position, grip.position + grip.up * 0.1f);
        Gizmos.color = Color.blue;
        Gizmos.DrawLine(grip.position, grip.position + grip.forward * 0.1f);

        if (handBone == null)
        {
            return;
        }

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(handBone.position, 0.025f);

        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(grip.position, handBone.position);

#if UNITY_EDITOR
        float distance = Vector3.Distance(grip.position, handBone.position);
        UnityEditor.Handles.Label(
            handBone.position + Vector3.up * 0.05f,
            $"{label} isAiming={isAiming} weight={weight:F2} dist={distance:F3}");
#endif
    }
}
