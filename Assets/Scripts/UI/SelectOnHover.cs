using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(Selectable))]
public class SelectOnHover : MonoBehaviour, IPointerEnterHandler
{
    public void OnPointerEnter(PointerEventData eventData)
    {
        Selectable selectable = GetComponent<Selectable>();
        if (selectable == null || !selectable.IsInteractable() || EventSystem.current == null) return;
        if (EventSystem.current.currentSelectedGameObject == gameObject) return;

        EventSystem.current.SetSelectedGameObject(gameObject);
        GameAudio.Play(GameSound.UiMove);
    }
}
