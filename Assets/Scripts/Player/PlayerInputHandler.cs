using System;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInputHandler : MonoBehaviour
{
    [SerializeField] private PlayerInput playerInput;

    private InputAction moveAction;
    private InputAction attackAction;
    private InputAction dashAction;
    private InputAction interactAction;
    private InputAction pauseAction;
    private InputAction ability1Action;
    private InputAction ability2Action;
    private InputAction skillsAction;
    private PlayerSeat seat;
    private int lastBlockedFrame = -10;

    public event Action<Vector2> OnMoveInput;
    public event Action OnAttackInput;
    public event Action OnDashInput;
    public event Action OnInteractInput;
    public event Action OnPauseInput;
    public event Action OnAbility1Input;
    public event Action OnAbility2Input;
    public event Action OnSkillsInput;

    public int PlayerIndex => playerInput != null ? playerInput.playerIndex : 0;
    public PlayerSeat Seat => seat;

    public bool IsInteractHeld => !IsBlocked && interactAction != null && interactAction.IsPressed();

    public bool IsBlocked => GamePause.IsPaused || (seat != null && seat.InMenu);

    public bool SkillsPressedThisFrame => WasPressed(skillsAction);

    private void Awake()
    {
        if (playerInput == null)
        {
            playerInput = GetComponentInParent<PlayerInput>();
        }

        if (playerInput == null || playerInput.actions == null)
        {
            Debug.LogError($"[PlayerInputHandler] {name} não tem PlayerInput (nem no pai) com actions configuradas. " +
                           "Personagens devem nascer pela PlayerSeat/ClassSelectionLobby.", this);
            return;
        }

        seat = playerInput.GetComponent<PlayerSeat>();
        InputActionAsset actions = playerInput.actions;
        moveAction = actions.FindAction("Player/Move");
        attackAction = actions.FindAction("Player/Attack");
        dashAction = actions.FindAction("Player/Dash");
        interactAction = actions.FindAction("Player/Interact");
        pauseAction = actions.FindAction("Player/Pause");
        ability1Action = actions.FindAction("Player/Ability1");
        ability2Action = actions.FindAction("Player/Ability2");
        skillsAction = actions.FindAction("Player/Skills");
    }

    private void OnDisable()
    {
        OnMoveInput?.Invoke(Vector2.zero);
    }

    private void Update()
    {
        if (IsBlocked)
        {
            lastBlockedFrame = Time.frameCount;
            OnMoveInput?.Invoke(Vector2.zero);
            return;
        }

        Vector2 inputVector = moveAction != null ? moveAction.ReadValue<Vector2>() : Vector2.zero;
        OnMoveInput?.Invoke(inputVector);

        if (Time.frameCount - lastBlockedFrame <= 1) return;

        if (WasPressed(attackAction)) OnAttackInput?.Invoke();
        if (WasPressed(dashAction)) OnDashInput?.Invoke();
        if (WasPressed(interactAction)) OnInteractInput?.Invoke();
        if (WasPressed(ability1Action)) OnAbility1Input?.Invoke();
        if (WasPressed(ability2Action)) OnAbility2Input?.Invoke();
        if (WasPressed(skillsAction)) OnSkillsInput?.Invoke();
        if (WasPressed(pauseAction)) OnPauseInput?.Invoke();
    }

    private static bool WasPressed(InputAction action)
    {
        return action != null && action.WasPressedThisFrame();
    }
}
