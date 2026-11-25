using System.Collections.Generic;
using UnityEngine;
using ProceduralToolkit;

namespace ProceduralRuins
{
    [CreateAssetMenu(fileName = "DungeonGenerationConfig", menuName = "Procedural Ruins/Dungeon Generation Config")]
    public class DungeonGenerationConfig : ScriptableObject
    {
        [Header("Grid")]
        [Tooltip("Tamanho de uma célula do grid em unidades do mundo. Confira no prefab de Floor_Standard.fbx importado.")]
        public float cellSize = 4f;

        [Tooltip("Ajuste fino de rotação em Y (graus) para paredes/portas, caso o pivot do FBX importado não esteja alinhado ao eixo +Z. Não afeta a correção de X abaixo.")]
        public float wallRotationOffset = 0f;

        [Tooltip("Correção EXTRA de rotação para os pisos. O gerador já preserva a rotação original do FBX (-90° em X do Blender); deixe zero, a menos que um modelo específico venha torto.")]
        public Vector3 floorRotationCorrection = Vector3.zero;

        [Tooltip("Correção EXTRA de rotação para as paredes. O gerador já preserva a rotação original do FBX; deixe zero.")]
        public Vector3 wallRotationCorrection = Vector3.zero;

        [Tooltip("Escala aplicada às peças. Os módulos do Ultimate Modular Ruins medem 2x2 unidades; com Cell Size 4, use 2.")]
        public float pieceScale = 2f;

        [Header("Câmera isométrica / colisão")]
        [Tooltip("Altura relativa das paredes viradas para a câmera (lado de baixo da tela). < 1 evita que escondam os jogadores.")]
        [Range(0.1f, 1f)] public float cameraFacingWallHeightScale = 0.35f;
        [Tooltip("Altura do colisor invisível de cada parede (independe da altura visual).")]
        public float wallColliderHeight = 3f;
        public float wallColliderThickness = 0.5f;

        [Header("Piso — Floor_Standard, Floor_Standard_Half, Floor_SquareLarge, Floor_Squares, Floor_Diamond")]
        public List<GameObject> floorPrefabs = new List<GameObject>();

        [Header("Piso decorativo (salas Treasure) — Floor_Tree")]
        public List<GameObject> floorDecorativePrefabs = new List<GameObject>();

        [Header("Piso danificado — Floor_Hole_Straight, Floor_Hole_Corner")]
        public List<GameObject> floorHolePrefabs = new List<GameObject>();

        [Header("Paredes — Wall, Wall_Overgrown")]
        public List<GameObject> wallPrefabs = new List<GameObject>();

        [Header("Paredes danificadas — Wall_Broken, Wall_Double_Broken, Wall_Half, Wall_Hole, Wall_Double_Hole")]
        public List<GameObject> wallBrokenPrefabs = new List<GameObject>();

        [Header("Paredes com janela — Window_Bars, Window_Bars_Overgrown, Window_Bars_Double_Overgrown, Window_Open, Window_Open_Double")]
        public List<GameObject> wallWindowPrefabs = new List<GameObject>();

        [Header("Portas entre sala e corredor — Doors_GothicArch, Doors_GothicArch_Covered, Doors_RoundArch, Doors_RoundArch_Covered")]
        public List<GameObject> doorPrefabs = new List<GameObject>();

        [Header("Transição de andar — Stairs, Stairs_2 (descida ao fim de cada andar, exceto o último)")]
        public List<GameObject> stairsPrefabs = new List<GameObject>();

        [Header("Transição de andar — Trapdoor (opcional, marca a chegada na sala Start de andares > 0)")]
        public GameObject trapdoorPrefab;

        [Header("Mobiliário / contêineres — Barrel, Crate, Chest, Chest_Gold, Bookcase_Empty, Bookcase_Full, Cart, Pot1/2/3, Pot1/2/3_Broken, Brick, Bricks")]
        public List<GameObject> furniturePropPrefabs = new List<GameObject>();

        [Header("Iluminação / decoração de parede — Torch, Candles_1, Candles_2, Flag_Wall, Flag_Wall2, Flag_GothicArch, Flag_RoundArch, Skull, Statue_Fox, Statue_Stag")]
        public List<GameObject> wallDecorPrefabs = new List<GameObject>();

        [Header("Tochas acesas (luz + chama) — TorchFlamePrefab, criado em Subterraneorum › Configurar Atmosfera e Tochas")]
        public GameObject torchPrefab;
        [Range(0, 4)] public int torchesPerRoomMin = 1;
        [Range(0, 4)] public int torchesPerRoomMax = 2;
        [Tooltip("Tochas a mais por sala do nível 2 em diante (subsolo).")]
        [Range(0, 3)] public int extraTorchesUnderground = 1;

        [Header("Vegetação — Bush_1x1, Bush_2x1, Bush_2x2, Bush_Large, Bush_Round, Tree_1, Tree_2, Tree_3, DeadTree_1, DeadTree_2, DeadTree_3, Grass")]
        public List<GameObject> vegetationPropPrefabs = new List<GameObject>();

        [Header("Regras de sala")]
        [Range(0f, 1f)] public float doorwayDoorChance = 0.6f;
        [Range(0f, 1f)] public float wallBrokenChance = 0.15f;
        [Range(0f, 1f)] public float wallWindowChance = 0.1f;
        [Tooltip("Chance de uma célula do miolo da sala virar piso quebrado (Floor_Hole_*). 0 = piso sempre inteiro.")]
        [Range(0f, 1f)] public float floorHoleChance = 0f;
        [Tooltip("Aumenta cada placa de piso na horizontal para elas se sobreporem e não deixarem frestas (as peças medem ~3,96 m numa célula de 4 m). Valores altos fazem as placas piscarem (z-fighting).")]
        [Range(1f, 1.05f)] public float floorTileOverlap = 1.012f;

        [Header("Entulho / decoração por sala")]
        [Range(0f, 1f)] public float propDensity = 0.6f;
        [Range(0, 8)] public int propsPerRoomMax = 4;

        public static GameObject Pick(List<GameObject> list)
        {
            if (list == null || list.Count == 0) return null;
            return list.GetRandom();
        }
    }
}
