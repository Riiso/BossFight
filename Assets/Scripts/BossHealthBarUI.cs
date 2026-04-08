using UnityEngine;
using UnityEngine.UI;

public class BossHealthBarUI : MonoBehaviour
{
    public BossHealth bossHealth;
    public Slider slider;

    void Start()
    {
        if (slider == null)
            slider = GetComponent<Slider>();

        if (bossHealth != null && slider != null)
        {
            slider.minValue = 0;
            slider.maxValue = bossHealth.maxHP;
            slider.value = bossHealth.currentHP;
        }
    }

    void Update()
    {
        if (bossHealth == null || slider == null) return;

        slider.value = bossHealth.currentHP;
    }
}