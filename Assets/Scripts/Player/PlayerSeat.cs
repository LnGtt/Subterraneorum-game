using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(PlayerInput))]
public class PlayerSeat : MonoBehaviour
{
    private static readonly List<PlayerSeat> all = new List<PlayerSeat>();

    [Tooltip("Inclinação mínima do analógico para navegar.")]
    [SerializeField] private float navigateThreshold = 0.5f;
    [Tooltip("Ignora input logo após entrar/abrir um menu, para o botão que abriu não confirmar nada.")]
    [SerializeField] private float inputGuardSeconds = 0.3f;

    private PlayerInput playerInput;
    private InputAction moveAction;
    private InputAction[] confirmActions = Array.Empty<InputAction>();
    private InputAction[] cancelActions = Array.Empty<InputAction>();
    private InputActionMap uiMap;
    private bool initialized;
    private bool listening;
    private Vector2Int heldDirection;
    private float acceptInputAt;
    private int menuDepth;

    public static IReadOnlyList<PlayerSeat> All => all;

    public PlayerInput Input => playerInput;
    public int PlayerIndex => playerInput != null ? playerInput.playerIndex : 0;

    public int CursorIndex { get; set; }

    public ClassDefinition SelectedClass { get; private set; }

    public bool IsConfirmed => SelectedClass != null;
    public GameObject Character { get; private set; }
    public bool HasCharacter => Character != null;

    public bool InMenu => !HasCharacter || menuDepth > 0;

    public event Action<PlayerSeat, Vector2Int> NavigateRequested;
    public event Action<PlayerSeat> ConfirmRequested;
    public event Action<PlayerSeat> CancelRequested;

    public string DeviceLabel
    {
        get
        {
            EnsureInitialized();
            if (playerInput == null) return "?";
            foreach (InputDevice device in playerInput.devices)
            {
                if (device is Gamepad) return "Controle";
                if (device is Keyboard) return "Teclado";
            }

            return playerInput.devices.Count > 0 ? playerInput.devices[0].displayName : "Sem dispositivo";
        }
    }

    public bool UsesGamepad
    {
        get
        {
            EnsureInitialized();
            if (playerInput == null) return false;
            foreach (InputDevice device in playerInput.devices)
            {
                if (device is Gamepad) return true;
            }

            return false;
        }
    }

    private void Awake()
    {
        EnsureInitialized();
    }

    public void EnsureInitialized()
    {
        if (initialized) return;

        playerInput = GetComponent<PlayerInput>();
        if (playerInput == null || playerInput.actions == null) return;

        initialized = true;
        acceptInputAt = Time.unscaledTime + inputGuardSeconds;
        name = $"PlayerSeat_P{playerInput.playerIndex + 1}";

        InputActionAsset actions = playerInput.actions;
        moveAction = actions.FindAction("Player/Move");
        uiMap = actions.FindActionMap("UI");
        confirmActions = FindAll(actions, "Player/Attack", "Player/Dash", "UI/Submit");
        cancelActions = FindAll(actions, "UI/Cancel", "Player/Pause");
        if (!all.Contains(this)) all.Add(this);
        RefreshListening();
    }

    private void OnEnable()
    {
        if (initialized && !all.Contains(this)) all.Add(this);
        RefreshListening();
    }

    private void OnDisable()
    {
        all.Remove(this);
        SetListening(false);
    }

    private void Update()
    {
        if (!listening || moveAction == null) return;

        Vector2 value = moveAction.ReadValue<Vector2>();
        var direction = new Vector2Int(
            value.x > navigateThreshold ? 1 : value.x < -navigateThreshold ? -1 : 0,
            value.y > navigateThreshold ? 1 : value.y < -navigateThreshold ? -1 : 0);

        if (direction != heldDirection)
        {
            var pressed = new Vector2Int(direction.x != heldDirection.x ? direction.x : 0, direction.y != heldDirection.y ? direction.y : 0);
            heldDirection = direction;
            if (pressed != Vector2Int.zero && InputAccepted()) NavigateRequested?.Invoke(this, pressed);
        }
    }

    public void Confirm(ClassDefinition definition)
    {
        SelectedClass = definition;
    }

    public void ClearConfirmation()
    {
        if (!HasCharacter) SelectedClass = null;
    }

    public void PushMenu()
    {
        menuDepth++;
        acceptInputAt = Time.unscaledTime + 0.2f;
        RefreshListening();
    }

    public void PopMenu()
    {
        menuDepth = Mathf.Max(0, menuDepth - 1);
        RefreshListening();
    }

    public GameObject SpawnCharacter(Vector3 position, Quaternion rotation)
    {
        if (HasCharacter) return Character;
        if (SelectedClass == null || SelectedClass.prefab == null)
        {
            Debug.LogError($"[PlayerSeat] {name}: classe sem prefab configurado.", this);
            return null;
        }

        Character = Instantiate(SelectedClass.prefab, position, rotation, transform);
        Character.name = $"{SelectedClass.prefab.name}_P{PlayerIndex + 1}";

        PlayerIdentity identity = Character.GetComponent<PlayerIdentity>();
        if (identity != null) identity.Bind(this, SelectedClass);

        RefreshListening();
        return Character;
    }

    private bool InputAccepted() => Time.unscaledTime >= acceptInputAt;

    private void RefreshListening()
    {
        SetListening(initialized && isActiveAndEnabled && InMenu);
    }

    private void SetListening(bool value)
    {
        if (value == listening) return;
        listening = value;
        heldDirection = Vector2Int.zero;
        if (value) acceptInputAt = Mathf.Max(acceptInputAt, Time.unscaledTime + 0.15f);

        foreach (InputAction action in confirmActions)
        {
            if (value) action.performed += HandleConfirm;
            else action.performed -= HandleConfirm;
        }

        foreach (InputAction action in cancelActions)
        {
            if (value) action.performed += HandleCancel;
            else action.performed -= HandleCancel;
        }

        if (uiMap != null)
        {
            if (value) uiMap.Enable();
            else uiMap.Disable();
        }
    }

    private int lastConfirmFrame = -1;
    private int lastCancelFrame = -1;

    private void HandleConfirm(InputAction.CallbackContext context)
    {
        if (!InputAccepted() || Time.frameCount == lastConfirmFrame) return;
        lastConfirmFrame = Time.frameCount;
        ConfirmRequested?.Invoke(this);
    }

    private void HandleCancel(InputAction.CallbackContext context)
    {
        if (!InputAccepted() || Time.frameCount == lastCancelFrame) return;
        lastCancelFrame = Time.frameCount;
        CancelRequested?.Invoke(this);
    }

    private static InputAction[] FindAll(InputActionAsset asset, params string[] paths)
    {
        var list = new List<InputAction>();
        foreach (string path in paths)
        {
            InputAction action = asset.FindAction(path);
            if (action != null) list.Add(action);
        }

        return list.ToArray();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        all.Clear();
    }
}
