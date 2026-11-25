using UnityEngine;

[CreateAssetMenu(menuName = "Subterraneorum/Tema de UI", fileName = "UiTheme")]
public class UiTheme : ScriptableObject
{
    [Tooltip("Painel claro (pergaminho).")] public Sprite panelLight;
    [Tooltip("Painel escuro (ardósia).")] public Sprite panelDark;
    [Tooltip("Painel escuro com cantos vermelhos.")] public Sprite panelRed;
    [Tooltip("Atlas card_uv.png do pacote playing-cards.")] public Texture2D cardAtlas;

    [Tooltip("Escala das bordas 9-slice (menor = borda mais grossa).")]
    public float borderScale = 0.4f;

    private static UiTheme current;

    public static UiTheme Current
    {
        get
        {
            if (current == null) current = Resources.Load<UiTheme>("Subterraneorum/UiTheme");
            return current;
        }
    }

    private const float CardWidth = 69f;
    private const float CardHeight = 93f;
    private const float CardTop = 186f;

    public Rect CardUV(Vector2Int cell)
    {
        float size = cardAtlas != null ? cardAtlas.width : 1024f;
        float x = cell.x * CardWidth / size;
        float y = 1f - (CardTop + (cell.y + 1) * CardHeight) / size;
        return new Rect(x, y, CardWidth / size, CardHeight / size);
    }
}
