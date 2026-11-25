using UnityEngine;
using UnityEngine.UI;

public class PlayerReviver : MonoBehaviour
{
    [SerializeField] private float reviveSeconds = 2f;
    [SerializeField] private float reviveRange = 2.2f;
    [SerializeField, Range(0.1f, 1f)] private float reviveHealth = 0.5f;

    private PlayerStatus status;
    private PlayerInputHandler input;
    private PlayerStatus target;
    private float progress;

    private GameObject indicator;
    private Image indicatorFill;

    public float Progress01 => reviveSeconds > 0f ? progress / reviveSeconds : 0f;

    private void Awake()
    {
        status = GetComponent<PlayerStatus>();
        input = GetComponent<PlayerInputHandler>();
        BuildIndicator();
    }

    private void OnEnable()
    {
        if (status == null) return;
        status.OnPlayerDied += ShowIndicator;
        status.OnPlayerRevived += HideIndicator;
    }

    private void OnDisable()
    {
        if (status == null) return;
        status.OnPlayerDied -= ShowIndicator;
        status.OnPlayerRevived -= HideIndicator;
    }

    private void Update()
    {
        if (status == null || status.IsDead || input == null || !input.IsInteractHeld)
        {
            ResetProgress();
            return;
        }

        PlayerStatus ally = FindDownedAlly();
        if (ally != target)
        {
            ResetProgress();
            target = ally;
        }

        if (target == null) return;

        progress += Time.deltaTime;
        PlayerReviver targetReviver = target.GetComponent<PlayerReviver>();
        if (targetReviver != null) targetReviver.SetIncoming(Progress01);

        if (progress >= reviveSeconds)
        {
            target.Revive(reviveHealth);
            ResetProgress();
        }
    }

    private PlayerStatus FindDownedAlly()
    {
        PlayerStatus best = null;
        float bestSqr = reviveRange * reviveRange;
        foreach (PlayerStatus other in PlayerRegistry.Players)
        {
            if (other == null || other == status || !other.IsDead) continue;
            Vector3 offset = other.transform.position - transform.position;
            offset.y = 0f;
            if (offset.sqrMagnitude <= bestSqr)
            {
                bestSqr = offset.sqrMagnitude;
                best = other;
            }
        }

        return best;
    }

    private void ResetProgress()
    {
        if (target != null)
        {
            PlayerReviver targetReviver = target.GetComponent<PlayerReviver>();
            if (targetReviver != null) targetReviver.SetIncoming(0f);
        }

        progress = 0f;
        target = null;
    }

    private void SetIncoming(float value)
    {
        if (indicatorFill != null) indicatorFill.transform.localScale = new Vector3(Mathf.Clamp01(value), 1f, 1f);
    }

    private void ShowIndicator()
    {
        SetIncoming(0f);
        if (indicator != null) indicator.SetActive(true);
    }

    private void HideIndicator()
    {
        SetIncoming(0f);
        if (indicator != null) indicator.SetActive(false);
    }

    private void BuildIndicator()
    {
        indicator = new GameObject("ReviveIndicator", typeof(RectTransform), typeof(Canvas), typeof(Billboard));
        indicator.transform.SetParent(transform, false);
        indicator.transform.localPosition = new Vector3(0f, 2.9f, 0f);
        indicator.transform.localScale = Vector3.one * 0.01f;
        Canvas canvas = indicator.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 5;
        indicator.GetComponent<RectTransform>().sizeDelta = new Vector2(300f, 90f);

        var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
        textGo.transform.SetParent(indicator.transform, false);
        RectTransform textRect = textGo.GetComponent<RectTransform>();
        textRect.anchorMin = new Vector2(0f, 0.4f);
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = textRect.offsetMax = Vector2.zero;
        Text text = textGo.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 26;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = new Color(1f, 0.85f, 0.4f);
        text.text = "CAÍDO! Segure E / Y perto";
        text.raycastTarget = false;

        var back = new GameObject("Back", typeof(RectTransform), typeof(Image));
        back.transform.SetParent(indicator.transform, false);
        RectTransform backRect = back.GetComponent<RectTransform>();
        backRect.anchorMin = new Vector2(0.1f, 0.05f);
        backRect.anchorMax = new Vector2(0.9f, 0.32f);
        backRect.offsetMin = backRect.offsetMax = Vector2.zero;
        back.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.7f);
        back.GetComponent<Image>().raycastTarget = false;

        var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fill.transform.SetParent(back.transform, false);
        RectTransform fillRect = fill.GetComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = new Vector2(3f, 3f);
        fillRect.offsetMax = new Vector2(-3f, -3f);
        fillRect.pivot = new Vector2(0f, 0.5f);
        indicatorFill = fill.GetComponent<Image>();
        indicatorFill.color = new Color(0.4f, 1f, 0.5f);
        indicatorFill.raycastTarget = false;

        SetIncoming(0f);
        indicator.SetActive(false);
    }
}
