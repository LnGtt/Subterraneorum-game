using UnityEngine;
using UnityEngine.UI;

public static class UiFactory
{
    private static Font font;

    public static Font Font => font != null ? font : font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

    public static readonly Color TextLight = new Color(0.95f, 0.92f, 0.85f);
    public static readonly Color TextDark = new Color(0.25f, 0.16f, 0.08f);
    public static readonly Color Good = new Color(0.45f, 1f, 0.45f);
    public static readonly Color Bad = new Color(1f, 0.45f, 0.35f);
    public static readonly Color Gold = new Color(1f, 0.85f, 0.4f);

    public static RectTransform Canvas(Transform parent, string name, int sortingOrder)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        go.transform.SetParent(parent, false);
        Canvas canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        CanvasScaler scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        return go.GetComponent<RectTransform>();
    }

    public static RectTransform Empty(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }

    public static Image Box(Transform parent, string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        Image image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    public static Image Panel(Transform parent, string name, Sprite sprite, Color tint)
    {
        Image image = Box(parent, name, tint);
        ApplySprite(image, sprite);
        return image;
    }

    public static void ApplySprite(Image image, Sprite sprite)
    {
        if (sprite == null) return;
        image.sprite = sprite;
        image.type = sprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
        UiTheme theme = UiTheme.Current;
        image.pixelsPerUnitMultiplier = theme != null ? theme.borderScale : 0.4f;
    }

    public static Text Label(Transform parent, string name, string content, int size, Color color, TextAnchor alignment = TextAnchor.MiddleCenter, bool bold = false)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        Text text = go.GetComponent<Text>();
        text.font = Font;
        text.fontSize = size;
        text.color = color;
        text.alignment = alignment;
        text.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
        text.text = content;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.supportRichText = true;
        return text;
    }

    public static Image Bar(Transform parent, string name, Color fillColor, out RectTransform root)
    {
        Image back = Box(parent, name, new Color(0f, 0f, 0f, 0.65f));
        root = back.rectTransform;
        Image fill = Box(back.transform, "Fill", fillColor);
        Stretch(fill.rectTransform, 2f);
        fill.rectTransform.pivot = new Vector2(0f, 0.5f);
        return fill;
    }

    public static void SetBar(Image fill, float value01)
    {
        if (fill == null) return;
        fill.rectTransform.localScale = new Vector3(Mathf.Clamp01(value01), 1f, 1f);
    }

    public static void Stretch(RectTransform rect, float inset = 0f)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
    }

    public static void Place(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size, Vector2? pivot = null)
    {
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = pivot ?? anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    public static void Region(RectTransform rect, Vector2 min, Vector2 max)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    public static string Colored(string text, Color color) => $"<color=#{ColorUtility.ToHtmlStringRGB(color)}>{text}</color>";
}
