using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public class end__button : MonoBehaviour
{
    public GameObject end__button_;
    public Camera mainCamera;

    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            Ray ray = mainCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
            RaycastHit hit;
            if (Physics.Raycast(ray, out hit))
            {
                if (hit.collider.gameObject == end__button_ || hit.collider.gameObject == gameObject)
                {
                    HandleClick();
                }
            }
        }
    }

    void HandleClick()
    {
        Debug.Log("end__button_ 클릭됨");   
        SceneManager.LoadScene("SampleScene");
    }
}
