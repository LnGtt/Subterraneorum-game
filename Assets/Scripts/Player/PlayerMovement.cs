using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Movimento")]
    [SerializeField] private float moveSpeed = 6f;
    [SerializeField] private float rotationSpeed = 15f;
    [SerializeField] private bool cameraRelative = true;

    [Header("Dash")]
    [SerializeField] private float dashDistance = 4f;
    [SerializeField] private float dashDuration = 0.18f;
    [SerializeField] private float dashCooldown = 0.8f;
    [Tooltip("Se verdadeiro, o jogador não recebe dano durante o dash.")]
    [SerializeField] private bool invulnerableWhileDashing = true;
    [Tooltip("Invulnerabilidade extra (s) depois do deslocamento do dash: cobre o resto da animação de rolar (~0,4 s no total).")]
    [SerializeField, Min(0f)] private float dashInvulnerabilityGrace = 0.2f;

    [Header("Referências")]
    [SerializeField] private Rigidbody rb;
    [SerializeField] private Animator animator;
    [SerializeField] private PlayerInputHandler inputHandler;

    private static readonly int IsMovingHash = Animator.StringToHash("isMoving");
    private static readonly int DashHash = Animator.StringToHash("dash");

    private Vector2 moveInput;
    private Vector3 lastMoveDirection = Vector3.forward;
    private bool isDashing;
    private float dashInvulnerableUntil;
    private float nextDashTime;
    private Transform cameraTransform;
    private PlayerStatus status;

    public bool IsInvulnerable => invulnerableWhileDashing && (isDashing || Time.time < dashInvulnerableUntil);
    public float DashCooldownRemaining => Mathf.Max(0f, nextDashTime - Time.time);
    public float DashCooldown => dashCooldown;

    public float SpeedMultiplier { get; set; } = 1f;

    public float StatusSpeedMultiplier { get; set; } = 1f;

    public void Configure(float speed, float dashCooldownSeconds)
    {
        moveSpeed = Mathf.Max(0.5f, speed);
        dashCooldown = Mathf.Max(0.1f, dashCooldownSeconds);
    }

    public void ResetDashCooldown()
    {
        nextDashTime = 0f;
    }

    private void Awake()
    {
        if (rb == null) rb = GetComponent<Rigidbody>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (inputHandler == null) inputHandler = GetComponent<PlayerInputHandler>();
        status = GetComponent<PlayerStatus>();

        rb.useGravity = false;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        rb.constraints = AliveConstraints;
    }

    private const RigidbodyConstraints AliveConstraints =
        RigidbodyConstraints.FreezePositionY | RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

    public void SetDowned(bool downed)
    {
        if (rb == null) return;

        if (!rb.isKinematic)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        rb.constraints = downed ? RigidbodyConstraints.FreezeAll : AliveConstraints;
    }

    private void OnEnable()
    {
        if (inputHandler != null)
        {
            inputHandler.OnMoveInput += HandleMoveInput;
            inputHandler.OnDashInput += TryDash;
        }
    }

    private void OnDisable()
    {
        if (inputHandler != null)
        {
            inputHandler.OnMoveInput -= HandleMoveInput;
            inputHandler.OnDashInput -= TryDash;
        }
    }

    private void FixedUpdate()
    {
        if (isDashing)
        {
            return;
        }

        Vector3 moveDirection = GetWorldDirection(moveInput);
        bool isMoving = moveDirection.sqrMagnitude > 0.0001f;

        Vector3 velocity = moveDirection * (moveSpeed * SpeedMultiplier * StatusSpeedMultiplier);
        velocity = PartyLeash.ConstrainVelocity(status, rb.position, velocity, Time.fixedDeltaTime);
        rb.linearVelocity = new Vector3(velocity.x, 0f, velocity.z);
        rb.angularVelocity = Vector3.zero;

        if (isMoving)
        {
            lastMoveDirection = moveDirection.normalized;
            Quaternion targetRotation = Quaternion.LookRotation(lastMoveDirection);
            rb.MoveRotation(Quaternion.Slerp(rb.rotation, targetRotation, rotationSpeed * Time.fixedDeltaTime));
        }

        if (animator != null)
        {
            animator.SetBool(IsMovingHash, isMoving);
        }
    }

    private Vector3 GetWorldDirection(Vector2 input)
    {
        Vector2 clamped = Vector2.ClampMagnitude(input, 1f);
        if (!cameraRelative)
        {
            return new Vector3(clamped.x, 0f, clamped.y);
        }

        if (cameraTransform == null && Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }

        if (cameraTransform == null)
        {
            return new Vector3(clamped.x, 0f, clamped.y);
        }

        Vector3 forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
        Vector3 right = Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.001f)
        {
            forward = Vector3.forward;
            right = Vector3.right;
        }

        return forward * clamped.y + right * clamped.x;
    }

    private void HandleMoveInput(Vector2 input)
    {
        moveInput = input;
    }

    private void TryDash()
    {
        if (!enabled || isDashing || Time.time < nextDashTime)
        {
            return;
        }

        Vector3 direction = GetWorldDirection(moveInput);
        if (direction.sqrMagnitude < 0.01f)
        {
            direction = transform.forward;
        }

        StartCoroutine(DashRoutine(direction.normalized));
    }

    private IEnumerator DashRoutine(Vector3 direction)
    {
        isDashing = true;
        nextDashTime = Time.time + dashCooldown;
        GameAudio.Play(GameSound.Dash);
        if (AnimatorHasParameter(DashHash)) animator.SetTrigger(DashHash);

        float speed = dashDistance / Mathf.Max(0.01f, dashDuration);
        float elapsed = 0f;
        rb.MoveRotation(Quaternion.LookRotation(direction));

        while (elapsed < dashDuration)
        {
            rb.linearVelocity = PartyLeash.ConstrainVelocity(status, rb.position, direction * speed, Time.fixedDeltaTime);
            elapsed += Time.fixedDeltaTime;
            yield return new WaitForFixedUpdate();
        }

        rb.linearVelocity = Vector3.zero;
        isDashing = false;
        dashInvulnerableUntil = Time.time + dashInvulnerabilityGrace;
    }

    private bool AnimatorHasParameter(int hash)
    {
        if (animator == null || !animator.isActiveAndEnabled || animator.runtimeAnimatorController == null) return false;
        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.nameHash == hash) return true;
        }

        return false;
    }

    public void StopMovement()
    {
        StopAllCoroutines();
        isDashing = false;
        moveInput = Vector2.zero;

        if (rb != null && !rb.isKinematic)
        {
            rb.linearVelocity = Vector3.zero;
        }

        if (animator != null)
        {
            animator.SetBool(IsMovingHash, false);
        }
    }

    public void FaceDirection(Vector3 direction)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f) return;
        lastMoveDirection = direction.normalized;
        Quaternion rotation = Quaternion.LookRotation(lastMoveDirection);
        rb.MoveRotation(rotation);
        transform.rotation = rotation;
    }

    public void Teleport(Vector3 position, Quaternion rotation)
    {
        StopMovement();
        rb.position = position;
        rb.rotation = rotation;
        transform.SetPositionAndRotation(position, rotation);
        Physics.SyncTransforms();
    }
}
