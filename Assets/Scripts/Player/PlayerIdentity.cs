using UnityEngine;

public class PlayerIdentity : MonoBehaviour
{
    [SerializeField] private ClassDefinition classDefinition;

    private bool applied;

    public ClassDefinition Class => classDefinition;
    public PlayerSeat Seat { get; private set; }
    public int PlayerIndex => Seat != null ? Seat.PlayerIndex : 0;
    public string PlayerLabel => $"J{PlayerIndex + 1}";

    private void Awake()
    {
        ApplyClass();
    }

    public void Bind(PlayerSeat seat, ClassDefinition definition)
    {
        Seat = seat;
        if (definition != null && definition != classDefinition)
        {
            classDefinition = definition;
            applied = false;
            ApplyClass();
        }
    }

    private void ApplyClass()
    {
        if (applied || classDefinition == null) return;
        applied = true;

        PlayerStatus status = GetComponent<PlayerStatus>();
        if (status != null) status.InitializeMaxHealth(classDefinition.maxHealth);

        PlayerMovement movement = GetComponent<PlayerMovement>();
        if (movement != null) movement.Configure(classDefinition.moveSpeed, classDefinition.dashCooldown);

        PlayerCombat combat = GetComponent<PlayerCombat>();
        if (combat != null) combat.Configure(classDefinition);
    }
}
