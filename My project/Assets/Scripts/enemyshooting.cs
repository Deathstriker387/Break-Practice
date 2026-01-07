using UnityEngine;

public class enemyshooting : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    public GameObject projectile;
    public Transform shootPoint;

    private float timer;
    private GameObject player;
    void Start()
    {
        player = GameObject.FindGameObjectWithTag("Player");
    }

    // Update is called once per frame
    void Update()
    {
        
        float distance = Vector2.Distance(transform.position, player.transform.position);

        if (distance > 5f)
        {
            timer += Time.deltaTime;

            if (timer >= 1f)
            {

                timer = 0f;
                shoot();
            }
        }

       
    }

    void shoot() { 
        Instantiate(projectile, shootPoint.position, Quaternion.identity);
    }
}
