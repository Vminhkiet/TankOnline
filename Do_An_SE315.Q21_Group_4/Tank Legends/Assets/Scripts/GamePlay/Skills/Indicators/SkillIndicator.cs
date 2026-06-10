using UnityEngine;

namespace Complete.Skills
{
    [RequireComponent(typeof(LineRenderer))]
    public class SkillIndicator : MonoBehaviour
    {
        private LineRenderer m_LineRenderer;
        private SkillData m_CurrentSkill;

        public int circleSegments = 36;
        public float heightOffset = 0.2f;

        private void Awake()
        {
            m_LineRenderer = GetComponent<LineRenderer>();
            m_LineRenderer.useWorldSpace = false;
            m_LineRenderer.enabled = false;
            m_LineRenderer.startWidth = 0.2f;
            m_LineRenderer.endWidth = 0.2f;
        }

        public void EnableIndicator(SkillData skill)
        {
            m_CurrentSkill = skill;
            m_LineRenderer.enabled = true;
            m_LineRenderer.startColor = skill.indicatorColor;
            m_LineRenderer.endColor = skill.indicatorColor;
            UpdateIndicatorShape();
        }

        public void DisableIndicator()
        {
            m_LineRenderer.enabled = false;
            m_CurrentSkill = null;
        }

        private void Update()
        {
            if (m_LineRenderer.enabled && m_CurrentSkill != null)
            {
                // In a real game, if TargetingType is Position, the indicator might follow the mouse.
                // For this example, we'll assume the Indicator is attached to the player or mouse cursor.
                // It just draws the shape around its local origin.
                UpdateIndicatorShape();
            }
        }

        private void UpdateIndicatorShape()
        {
            if (m_CurrentSkill == null) return;

            switch (m_CurrentSkill.shapeType)
            {
                case ShapeType.Circle:
                    DrawCircle(m_CurrentSkill.radius);
                    break;
                case ShapeType.Line:
                    DrawLine(m_CurrentSkill.length);
                    break;
                case ShapeType.Cone:
                    DrawCone(m_CurrentSkill.radius, m_CurrentSkill.angle);
                    break;
                case ShapeType.None:
                default:
                    m_LineRenderer.positionCount = 0;
                    break;
            }
        }

        private void DrawCircle(float radius)
        {
            m_LineRenderer.positionCount = circleSegments + 1;
            float angle = 0f;
            for (int i = 0; i < (circleSegments + 1); i++)
            {
                float x = Mathf.Sin(Mathf.Deg2Rad * angle) * radius;
                float z = Mathf.Cos(Mathf.Deg2Rad * angle) * radius;

                m_LineRenderer.SetPosition(i, new Vector3(x, heightOffset, z));
                angle += (360f / circleSegments);
            }
        }

        private void DrawLine(float length)
        {
            m_LineRenderer.positionCount = 2;
            m_LineRenderer.SetPosition(0, new Vector3(0, heightOffset, 0));
            m_LineRenderer.SetPosition(1, new Vector3(0, heightOffset, length));
        }

        private void DrawCone(float radius, float angle)
        {
            // Draw an outline of a cone (pie slice)
            int arcSegments = Mathf.CeilToInt(circleSegments * (angle / 360f));
            m_LineRenderer.positionCount = arcSegments + 3;

            // Start at origin
            m_LineRenderer.SetPosition(0, new Vector3(0, heightOffset, 0));

            float startAngle = -angle / 2f;
            float angleStep = angle / arcSegments;

            for (int i = 0; i <= arcSegments; i++)
            {
                float currentAngle = startAngle + (angleStep * i);
                float x = Mathf.Sin(Mathf.Deg2Rad * currentAngle) * radius;
                float z = Mathf.Cos(Mathf.Deg2Rad * currentAngle) * radius;
                m_LineRenderer.SetPosition(i + 1, new Vector3(x, heightOffset, z));
            }

            // Return to origin
            m_LineRenderer.SetPosition(arcSegments + 2, new Vector3(0, heightOffset, 0));
        }
    }
}
