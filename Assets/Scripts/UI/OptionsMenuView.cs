using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public sealed class OptionsMenuView
{
    private readonly RectTransform root;
    private readonly Text title;
    private readonly Text body;
    private readonly List<Text> optionTexts = new List<Text>();
    private readonly List<(string label, Action action)> options = new List<(string, Action)>();
    private readonly List<PlayerSeat> listeners = new List<PlayerSeat>();
    private Action onCancel;
    private int cursor;

    public bool IsOpen => root.gameObject.activeSelf;

    public OptionsMenuView(Transform parent, string name, int sortingOrder)
    {
        root = UiFactory.Canvas(parent, name, sortingOrder);
        Image dim = UiFactory.Box(root, "Dim", new Color(0f, 0f, 0f, 0.7f));
        UiFactory.Stretch(dim.rectTransform);

        UiTheme theme = UiTheme.Current;
        Image panel = UiFactory.Panel(root, "Panel", theme != null ? theme.panelDark : null, Color.white);
        UiFactory.Place(panel.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(860f, 640f));

        title = UiFactory.Label(panel.rectTransform, "Title", "", 60, UiFactory.Gold, TextAnchor.MiddleCenter, bold: true);
        UiFactory.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -70f), new Vector2(800f, 90f));
        body = UiFactory.Label(panel.rectTransform, "Body", "", 26, UiFactory.TextLight, TextAnchor.UpperCenter);
        UiFactory.Place(body.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -200f), new Vector2(780f, 180f));

        for (int i = 0; i < 6; i++)
        {
            Text option = UiFactory.Label(panel.rectTransform, $"Option_{i}", "", 34, UiFactory.TextLight, TextAnchor.MiddleCenter, bold: true);
            UiFactory.Place(option.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 290f - i * 52f), new Vector2(700f, 48f));
            optionTexts.Add(option);
        }

        root.gameObject.SetActive(false);
    }

    public void Open(string titleText, string bodyText, IEnumerable<PlayerSeat> seats, Action cancel, params (string label, Action action)[] entries)
    {
        Open(titleText, bodyText, seats, cancel, 0, entries);
    }

    public void Open(string titleText, string bodyText, IEnumerable<PlayerSeat> seats, Action cancel, int startCursor, params (string label, Action action)[] entries)
    {
        Close();
        title.text = titleText;
        body.text = bodyText;
        options.Clear();
        options.AddRange(entries);
        onCancel = cancel;
        cursor = Mathf.Clamp(startCursor, 0, Mathf.Max(0, options.Count - 1));

        foreach (PlayerSeat seat in seats)
        {
            if (seat == null) continue;
            listeners.Add(seat);
            seat.PushMenu();
            seat.NavigateRequested += HandleNavigate;
            seat.ConfirmRequested += HandleConfirm;
            seat.CancelRequested += HandleCancel;
        }

        root.gameObject.SetActive(true);
        Refresh();
    }

    public void Close()
    {
        foreach (PlayerSeat seat in listeners)
        {
            if (seat == null) continue;
            seat.NavigateRequested -= HandleNavigate;
            seat.ConfirmRequested -= HandleConfirm;
            seat.CancelRequested -= HandleCancel;
            seat.PopMenu();
        }

        listeners.Clear();
        root.gameObject.SetActive(false);
    }

    public void Choose(int index)
    {
        if (index < 0 || index >= options.Count) return;
        cursor = index;
        options[index].action?.Invoke();
    }

    private void HandleNavigate(PlayerSeat _, Vector2Int direction)
    {
        if (direction.y == 0 || options.Count == 0) return;
        cursor = (cursor - direction.y + options.Count) % options.Count;
        GameAudio.Play(GameSound.UiMove);
        Refresh();
    }

    private void HandleConfirm(PlayerSeat _)
    {
        GameAudio.Play(GameSound.UiConfirm);
        Choose(cursor);
    }

    private void HandleCancel(PlayerSeat _)
    {
        onCancel?.Invoke();
    }

    private void Refresh()
    {
        for (int i = 0; i < optionTexts.Count; i++)
        {
            bool visible = i < options.Count;
            optionTexts[i].gameObject.SetActive(visible);
            if (!visible) continue;
            UiFactory.Place(optionTexts[i].rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 34f + (options.Count - 1 - i) * 52f), new Vector2(700f, 48f));
            bool selected = i == cursor;
            optionTexts[i].text = selected ? $"> {options[i].label} <" : options[i].label;
            optionTexts[i].color = selected ? UiFactory.Gold : UiFactory.TextLight;
        }
    }
}
