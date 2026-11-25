using Unity.Cinemachine;
using UnityEngine;

namespace Subterraneorum.CameraSystem
{
    [RequireComponent(typeof(CinemachineCamera))]
    public class CameraManager : MonoBehaviour
    {
        public static CameraManager Instance { get; private set; }

        [SerializeField] private CinemachineCamera virtualCamera;
        [SerializeField] private CinemachineTargetGroup targetGroup;
        [SerializeField] private CinemachineConfiner3D cameraConfiner;

        [Header("Enquadramento 2.5D")]
        [Tooltip("Se ligado, sobrescreve o FollowOffset do CinemachineFollow. Ângulo fixo = câmera travada.")]
        [SerializeField] private bool overrideFollowOffset = true;
        [SerializeField] private Vector3 followOffset = new Vector3(0f, 14f, -10f);
        [Tooltip("Afasta a câmera para caber todos os jogadores vivos.")]
        [SerializeField] private bool frameAllPlayers = true;

        [Header("Zoom")]
        [Tooltip("Fração da tela que o grupo de jogadores ocupa. Menor = mais espaço em volta.")]
        [SerializeField, Range(0.2f, 0.9f)] private float framingSize = 0.5f;
        [Tooltip("Quanto a câmera pode se AFASTAR além da posição base para caber todo mundo. Ela nunca se aproxima além da base.")]
        [SerializeField] private float maxZoomOut = 22f;
        [Tooltip("Raio mínimo de cada jogador no enquadramento (m). Evita que 1 jogador sozinho \"encha\" a tela.")]
        [SerializeField] private float minTargetRadius = 3f;

        private CinemachineFollow follow;
        private float currentDistance;

        private void LateUpdate()
        {
            if (!frameAllPlayers || follow == null || !overrideFollowOffset) return;

            float baseDistance = followOffset.magnitude;
            float desired = baseDistance;
            CinemachineTargetGroup group = ResolveTargetGroup();
            if (group != null && !group.IsEmpty && virtualCamera != null)
            {
                float radius = group.Sphere.radius;
                float halfFov = virtualCamera.Lens.FieldOfView * 0.5f * Mathf.Deg2Rad;
                float needed = radius / (Mathf.Tan(halfFov) * Mathf.Max(0.1f, framingSize));
                desired = Mathf.Clamp(needed, baseDistance, baseDistance + Mathf.Max(0f, maxZoomOut));
            }

            float speed = desired > currentDistance ? 3f : 1f;
            currentDistance = Mathf.Lerp(currentDistance, desired, 1f - Mathf.Exp(-speed * Time.unscaledDeltaTime));
            follow.FollowOffset = followOffset.normalized * currentDistance;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;
            if (virtualCamera == null) virtualCamera = GetComponent<CinemachineCamera>();
            if (cameraConfiner == null) cameraConfiner = GetComponent<CinemachineConfiner3D>();

            ConfigureCamera();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void ConfigureCamera()
        {
            CinemachineTargetGroup group = ResolveTargetGroup();
            if (group != null && virtualCamera != null)
            {
                virtualCamera.Follow = group.transform;
                virtualCamera.LookAt = group.transform;
            }

            follow = GetComponent<CinemachineFollow>();
            if (overrideFollowOffset && follow != null) follow.FollowOffset = followOffset;
            currentDistance = followOffset.magnitude;

            CinemachineGroupFraming framing = GetComponent<CinemachineGroupFraming>();
            if (framing != null) framing.enabled = false;

            CinemachineImpulseListener listener = GetComponent<CinemachineImpulseListener>();
            if (listener == null) listener = gameObject.AddComponent<CinemachineImpulseListener>();
            listener.Gain = 1f;

            if (cameraConfiner != null) cameraConfiner.enabled = false;
        }

        public void AddTarget(Transform targetTransform, float weight, float radius)
        {
            CinemachineTargetGroup group = ResolveTargetGroup();
            if (targetTransform == null || group == null || group.FindMember(targetTransform) >= 0)
            {
                return;
            }

            group.AddMember(targetTransform, weight, Mathf.Max(radius, minTargetRadius));
        }

        public void RemoveTarget(Transform targetTransform)
        {
            CinemachineTargetGroup group = ResolveTargetGroup();
            if (targetTransform == null || group == null)
            {
                return;
            }

            group.RemoveMember(targetTransform);
        }

        public void SnapToTargets()
        {
            if (virtualCamera != null)
            {
                virtualCamera.PreviousStateIsValid = false;
            }
        }

        private CinemachineTargetGroup ResolveTargetGroup()
        {
            if (targetGroup != null)
            {
                return targetGroup;
            }

            GameObject gameController = GameObject.FindGameObjectWithTag("GameController");
            if (gameController != null)
            {
                targetGroup = gameController.GetComponent<CinemachineTargetGroup>();
            }

            if (targetGroup == null)
            {
                targetGroup = FindAnyObjectByType<CinemachineTargetGroup>();
            }

            return targetGroup;
        }
    }
}
