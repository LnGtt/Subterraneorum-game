using Unity.Cinemachine;
using UnityEngine;

namespace Subterraneorum.CameraSystem
{
    public class CameraTargetRegistrar : MonoBehaviour
    {
        [SerializeField] private float targetWeight = 1f;
        [SerializeField] private float targetRadius = 1f;
        [SerializeField] private CinemachineTargetGroup targetGroup;

        private PlayerStatus status;

        private void Awake()
        {
            status = GetComponent<PlayerStatus>();
        }

        private void OnEnable()
        {
            if (status != null)
            {
                status.OnPlayerDied += Unregister;
                status.OnPlayerRevived += Register;
            }
        }

        private void OnDisable()
        {
            if (status != null)
            {
                status.OnPlayerDied -= Unregister;
                status.OnPlayerRevived -= Register;
            }

            Unregister();
        }

        private void Start()
        {
            if (status == null || !status.IsDead)
            {
                Register();
            }
        }

        private void Register()
        {
            if (CameraManager.Instance != null)
            {
                CameraManager.Instance.AddTarget(transform, targetWeight, targetRadius);
                return;
            }

            CinemachineTargetGroup group = ResolveTargetGroup();
            if (group != null && group.FindMember(transform) < 0)
            {
                group.AddMember(transform, targetWeight, targetRadius);
            }
        }

        private void Unregister()
        {
            if (CameraManager.Instance != null)
            {
                CameraManager.Instance.RemoveTarget(transform);
                return;
            }

            CinemachineTargetGroup group = ResolveTargetGroup();
            if (group != null)
            {
                group.RemoveMember(transform);
            }
        }

        private CinemachineTargetGroup ResolveTargetGroup()
        {
            if (targetGroup != null)
            {
                return targetGroup;
            }

            GameObject gameController = GameObject.FindGameObjectWithTag("GameController");
            targetGroup = gameController != null ? gameController.GetComponent<CinemachineTargetGroup>() : null;
            return targetGroup;
        }
    }
}
