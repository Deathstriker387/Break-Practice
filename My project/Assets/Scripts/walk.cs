using UnityEngine;

public class walk : MonoBehaviour
{
    [Header("Rope - DO NOT MODIFY")]
    public Vector2 ropeHook;
    public bool isSwinging;

    [Header("Movement")]
    public float moveSpeed = 7f;

    [Header("Jump")]
    public float jumpForce = 15f;
    public float jumpSustainForce = 1.2f;
    public float jumpSustainTime = 0.12f;
    public float jumpCutMultiplier = 0.5f;

    [Header("Forgiveness")]
    public float coyoteTime = 0.1f;      // late jump forgiveness
    public float jumpBufferTime = 0.1f;  // early input forgiveness

    [Header("Ground Probe")]
    public Transform groundCheckPoint;
    public float groundCheckRadius = 0.1f;
    public LayerMask groundLayer;

    private Rigidbody2D rb;
    private float moveInput;

    private bool isGrounded;
    private bool isJumping;
    private bool jumpUsed;          // single jump per grounded session

    private float coyoteTimer;
    private float bufferTimer;
    private float sustainTimer;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    void Update()
    {
        moveInput = Input.GetAxisRaw("Horizontal");

        // --- Jump buffer ---
        bufferTimer -= Time.deltaTime;
        if (Input.GetKeyDown(KeyCode.Space))
            bufferTimer = jumpBufferTime;

        // --- Execute jump if allowed ---
        if (bufferTimer > 0 && coyoteTimer > 0 && !isJumping && !jumpUsed)
        {
            Jump();
        }

        // --- Short jump on release ---
        if (Input.GetKeyUp(KeyCode.Space))
        {
            JumpCut();
        }
    }

    void FixedUpdate()
    {
        // --- Ground probe ---
        isGrounded = Physics2D.OverlapCircle(groundCheckPoint.position, groundCheckRadius, groundLayer);

        // --- Reset jumpUsed whenever grounded ---
        if (isGrounded)
        {
            jumpUsed = false;       // <-- key fix
            isJumping = false;
            coyoteTimer = coyoteTime;
        }
        else
        {
            coyoteTimer -= Time.fixedDeltaTime;
        }

        // --- Horizontal movement ---
        if (!isSwinging)
        {
            if (isGrounded)
            {
                // Sharp ground control (Hollow Knight style)
                rb.linearVelocity = new Vector2(moveInput * moveSpeed, rb.linearVelocity.y);
            }
            else
            {
                // Air control without killing momentum
                rb.AddForce(Vector2.right * moveInput * moveSpeed * 0.6f, ForceMode2D.Force);
            }
        }

        // --- Jump sustain ---
        if (isJumping && sustainTimer > 0)
        {
            rb.AddForce(Vector2.up * jumpSustainForce, ForceMode2D.Force);
            sustainTimer -= Time.fixedDeltaTime;
        }
    }

    void Jump()
    {
        // Reset vertical velocity for consistent jump
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, 0f);
        rb.AddForce(Vector2.up * jumpForce, ForceMode2D.Impulse);

        isJumping = true;
        sustainTimer = jumpSustainTime;
        jumpUsed = true;      // mark jump as used
        bufferTimer = 0;
        coyoteTimer = 0;
    }

    void JumpCut()
    {
        if (rb.linearVelocity.y > 0)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, rb.linearVelocity.y * jumpCutMultiplier);
        }

        sustainTimer = 0;
        isJumping = false;
    }
}
