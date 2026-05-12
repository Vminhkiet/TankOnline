using UnityEngine;

public class UIFloat : MonoBehaviour
{
    public float amplitude = 5f;
    public float speed = 2f;

    private Vector2 startPos;

    void Start()
    {
        startPos = ((RectTransform)transform).anchoredPosition;
    }

    void Update()
    {
        float y = Mathf.Sin(Time.time * speed) * amplitude;
        ((RectTransform)transform).anchoredPosition = startPos + new Vector2(0, y);
    }
}