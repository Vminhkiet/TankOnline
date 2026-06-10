using UnityEngine;

namespace Complete.Skills
{
    [ExecuteAlways]
    [RequireComponent(typeof(LineRenderer))]
    public class SkillShapeVisualizer : MonoBehaviour
    {
        [Header("Tweak these values to visualize shapes")]
        public ShapeType shapeType = ShapeType.Circle;
        
        [Tooltip("Bán kính cho Circle hoặc Cone")]
        public float radius = 5f;
        
        [Tooltip("Độ dài cho Line")]
        public float length = 10f;
        
        [Tooltip("Góc mở cho Cone")]
        [Range(0f, 360f)]
        public float angle = 45f;

        [Header("Appearance")]
        public Color lineColor = Color.green;
        public float lineWidth = 0.1f;
        public int circleSegments = 36;
        public float heightOffset = 0.1f;

        private LineRenderer m_LineRenderer;

        private void OnEnable()
        {
            m_LineRenderer = GetComponent<LineRenderer>();
            UpdateShape();
        }

        private void Update()
        {
            // Trong Editor (khi không play), update liên tục để preview
            if (!Application.isPlaying)
            {
                UpdateShape();
            }
        }

        private void OnValidate()
        {
            UpdateShape();
        }

        private void UpdateShape()
        {
            if (m_LineRenderer == null) return;
            
            m_LineRenderer.useWorldSpace = false;
            m_LineRenderer.startColor = lineColor;
            m_LineRenderer.endColor = lineColor;
            m_LineRenderer.startWidth = lineWidth;
            m_LineRenderer.endWidth = lineWidth;

            switch (shapeType)
            {
                case ShapeType.Circle:
                    DrawCircle(radius);
                    break;
                case ShapeType.Line:
                    DrawLine(length);
                    break;
                case ShapeType.Cone:
                    DrawCone(radius, angle);
                    break;
                default:
                    m_LineRenderer.positionCount = 0;
                    break;
            }
        }

        private void DrawCircle(float r)
        {
            m_LineRenderer.positionCount = circleSegments + 1;
            float ang = 0f;
            for (int i = 0; i <= circleSegments; i++)
            {
                float x = Mathf.Sin(Mathf.Deg2Rad * ang) * r;
                float z = Mathf.Cos(Mathf.Deg2Rad * ang) * r;
                m_LineRenderer.SetPosition(i, new Vector3(x, heightOffset, z));
                ang += (360f / circleSegments);
            }
        }

        private void DrawLine(float l)
        {
            m_LineRenderer.positionCount = 2;
            m_LineRenderer.SetPosition(0, new Vector3(0, heightOffset, 0));
            m_LineRenderer.SetPosition(1, new Vector3(0, heightOffset, l));
        }

        private void DrawCone(float r, float ang)
        {
            int arcSegments = Mathf.Max(3, Mathf.CeilToInt(circleSegments * (ang / 360f)));
            m_LineRenderer.positionCount = arcSegments + 3;
            
            // Tâm
            m_LineRenderer.SetPosition(0, new Vector3(0, heightOffset, 0));

            float startAngle = -ang / 2f;
            float angleStep = ang / arcSegments;
            
            for (int i = 0; i <= arcSegments; i++)
            {
                float currentAngle = startAngle + (angleStep * i);
                float x = Mathf.Sin(Mathf.Deg2Rad * currentAngle) * r;
                float z = Mathf.Cos(Mathf.Deg2Rad * currentAngle) * r;
                m_LineRenderer.SetPosition(i + 1, new Vector3(x, heightOffset, z));
            }

            // Về tâm
            m_LineRenderer.SetPosition(arcSegments + 2, new Vector3(0, heightOffset, 0));
        }
    }
}
