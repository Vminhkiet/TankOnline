using UnityEngine;

namespace Complete
{
    public class TankAnimation : MonoBehaviour
    {
        [Header("Components")]
        public Animator m_Animator;                 // Reference to the Animator component

        [Header("Parameters")]
        public string m_AnimMoveParam = "IsMoving"; // Parameter for moving state
        public string m_AnimShootParam = "Shoot";   // Parameter for shooting
        public string m_AnimDieParam = "Die";       // Parameter for death

        private bool m_IsMoving = false;
        private bool m_IsShooting = false;
        private float m_RemoteShootTimer = 0f;

        public void SetMoving(bool isMoving)
        {
            if (m_Animator == null) return;
            
            m_IsMoving = isMoving;
            m_Animator.SetBool(m_AnimMoveParam, m_IsMoving);
        }

        public void SetShooting(bool isShooting)
        {
            if (m_Animator == null) return;

            m_IsShooting = isShooting;
            m_Animator.SetBool(m_AnimShootParam, m_IsShooting);
        }

        public void PlayRemoteShoot()
        {
            if (m_Animator == null) return;
            
            m_IsShooting = true;
            m_Animator.SetBool(m_AnimShootParam, true);
            m_RemoteShootTimer = 0.3f; // Giữ animation bắn trong 0.3 giây
        }

        private void Update()
        {
            if (m_RemoteShootTimer > 0f)
            {
                m_RemoteShootTimer -= Time.deltaTime;
                if (m_RemoteShootTimer <= 0f)
                {
                    m_IsShooting = false;
                    if (m_Animator != null) m_Animator.SetBool(m_AnimShootParam, false);
                }
            }
        }

        public void PlayDie()
        {
            if (m_Animator == null) return;
            
            m_Animator.SetTrigger(m_AnimDieParam);
        }
    }
}
