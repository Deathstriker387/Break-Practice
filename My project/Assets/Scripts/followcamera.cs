using UnityEngine;

public class followcamera : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created

   

    public Transform PLAYER;     
    public float Speed = 5f; 
    public float xOffset = 0f;     

    void Update()
    {
        

        Vector3 newPosition = transform.position;
        newPosition.x = Mathf.Lerp(
            transform.position.x,
            PLAYER.position.x + xOffset,
            Speed * Time.deltaTime
        );

        transform.position = newPosition;
    }
}


