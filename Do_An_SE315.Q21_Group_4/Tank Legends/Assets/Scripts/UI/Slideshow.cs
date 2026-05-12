using UnityEngine;
using UnityEngine.UI;

public class Slideshow : MonoBehaviour
{
    public Image displayImage;
    public Sprite[] images;
    public float delay = 2f;

    int index = 0;

    void Start()
    {
        InvokeRepeating("NextImage", delay, delay);
    }

    void NextImage()
    {
        index = (index + 1) % images.Length;
        displayImage.sprite = images[index];
    }
}
