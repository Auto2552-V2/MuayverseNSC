using UnityEngine;

public class UIManager : MonoBehaviour
{
    // E F G H
    public GameObject[] panels;

    // index = 0(E), 1(F), 2(G), 3(H)
    public void ShowPanel(int index)
    {
        for (int i = 0; i < panels.Length; i++)
        {
            panels[i].SetActive(i == index);
        }
    }
}