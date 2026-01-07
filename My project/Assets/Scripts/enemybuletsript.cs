using UnityEngine;

public class enemybuletsript : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private GameObject player;
    private Rigidbody2D rb; 
    public float force = 2f;
    private float timer;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        player = GameObject.FindGameObjectWithTag("Player");

        Vector3 direction = player.transform.position - transform.position;
        rb.linearVelocity= new Vector2(direction.x, direction.y).normalized * force; 

        float rotation = Mathf.Atan2(-direction.y, -direction.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0f, 0f, rotation);  
    }

    // Update is called once per frame
    void Update()
    {
        timer+= Time.deltaTime;

        if (timer >= 10f)
        {
            Destroy(gameObject);
        }
    }

    void OnTriggerEnter2D(Collider2D other) {
        if (other.CompareTag("Player"))
        {
            Destroy(gameObject);
        }
    }
}
