using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class LookAtMouse : MonoBehaviour
{
    private Transform m_transform;
    private Camera mainCamera;

    [Header("Movement Settings")]
    public float moveSpeed = 15f;
    private Vector2 targetPosition;
    private bool moving = false;

    [Header("Rope Settings")]
    public Transform playerTransform;
    public float maxLineLength = 10f;
    [SerializeField] private int _numOfRopeSegments = 50;
    [SerializeField] private float _ropeSegmentLength = 0.2f;

    [Header("Rope Physics")]
    [SerializeField] private LayerMask _collisionMask;
    [SerializeField] private float _collisionRadius = 0.1f;
    [SerializeField] private float _constraintStiffness = 0.95f; // How taut the rope is (0.9-1.0 recommended)

    [Header("Rope Constraints")]
    [SerializeField] private int _numOfConstraintRuns = 80;
    [SerializeField] private int _collisionSegmentInterval = 2;

    [Header("Collision Settings")]
    public LayerMask grappleMask;
    public string grappleTag = "Grapple";

    [Header("Grapple Pull Settings")]
    [SerializeField] private float pullForce = 25f;
    [SerializeField] private float pullSpeed = 10f;
    [SerializeField] private bool maintainMomentum = true;
    [SerializeField] private bool disablePlayerControlDuringPull = true;

    private Collider2D hookCollider;
    private Collider2D playerCollider;
    private Rigidbody2D hookRb;
    private Rigidbody2D playerRb;
    private bool retracting = false;
    private bool isGrappled = false;
    private bool isPullingPlayer = false;

    // Store player's original state
    private RigidbodyType2D originalBodyType;
    private MonoBehaviour playerMovementScript;

    // Rope system
    private LineRenderer _lineRenderer;
    private List<RopeSegment> _ropeSegments = new List<RopeSegment>();
    private int _activeSegments = 0;

    // Optimization caches
    private Vector2 lastPlayerPos;
    private Vector2 lastHookPos;

    public struct RopeSegment
    {
        public Vector2 CurrentPosition;
        public Vector2 OldPosition;

        public RopeSegment(Vector2 pos)
        {
            CurrentPosition = pos;
            OldPosition = pos;
        }
    }

    void Start()
    {
        m_transform = transform;
        mainCamera = Camera.main;
        hookCollider = GetComponent<Collider2D>();
        hookRb = GetComponent<Rigidbody2D>();
        playerCollider = playerTransform.GetComponent<Collider2D>();
        playerRb = playerTransform.GetComponent<Rigidbody2D>();
        targetPosition = hookRb.position;

        // Setup LineRenderer for rope
        _lineRenderer = playerTransform.GetComponent<LineRenderer>();
        if (_lineRenderer == null)
            _lineRenderer = playerTransform.gameObject.AddComponent<LineRenderer>();

        _lineRenderer.positionCount = 0;

        // Initialize position caches
        lastPlayerPos = playerTransform.position;
        lastHookPos = hookRb.position;

        // Ignore collision between hook and player
        if (hookCollider != null && playerCollider != null)
            Physics2D.IgnoreCollision(hookCollider, playerCollider, true);

        // Set hook collider to trigger mode to detect collisions
        if (hookCollider != null)
            hookCollider.isTrigger = true;

        // Validate player rigidbody setup
        if (playerRb == null)
        {
            Debug.LogError("Player Rigidbody2D is missing! Add Rigidbody2D to player.");
        }
        else
        {
            Debug.Log($"Player RB found - BodyType: {playerRb.bodyType}, Mass: {playerRb.mass}, Constraints: {playerRb.constraints}");

            // Store original body type
            originalBodyType = playerRb.bodyType;

            // Ensure player rigidbody is Dynamic
            if (playerRb.bodyType != RigidbodyType2D.Dynamic)
            {
                Debug.LogWarning("Player Rigidbody2D must be Dynamic! Setting it now...");
                playerRb.bodyType = RigidbodyType2D.Dynamic;
            }

            // Try to find player movement script (adjust script name to match yours)
            playerMovementScript = playerTransform.GetComponent<MonoBehaviour>();
            if (playerMovementScript != null)
            {
                Debug.Log($"Found player script: {playerMovementScript.GetType().Name}");
            }
        }
    }

    private void LAMouse()
    {
        Vector2 direction;

        if (retracting)
        {
            // When retracting, face backward (opposite of movement direction)
            direction = hookRb.position - (Vector2)playerTransform.position;
        }
        else
        {
            // When shooting, face toward target
            direction = targetPosition - hookRb.position;
        }

        if (direction == Vector2.zero) return;

        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        m_transform.rotation = Quaternion.AngleAxis(angle - 90f, Vector3.forward);
    }

    void FixedUpdate()
    {
        HandleMovement();

        // Simulate rope physics only when rope exists
        if (_activeSegments > 0)
        {
            for (int i = 0; i < _numOfConstraintRuns; i++)
            {
                ApplyRopeConstraints();

                if (i % _collisionSegmentInterval == 0)
                {
                    HandleRopeCollisions();
                }
            }
        }

        // Pull player towards hook when grappled
        if (isPullingPlayer && isGrappled && playerRb != null)
        {
            PullPlayerTowardsHook();
            AdjustRopeWhilePulling();
        }
        else if (isPullingPlayer)
        {
            // Debug why pulling isn't happening
            Debug.LogWarning($"Pull blocked - isPulling: {isPullingPlayer}, isGrappled: {isGrappled}, playerRb: {playerRb != null}");
        }
    }

    private void HandleMovement()
    {
        if (!moving) return;

        LAMouse();

        Vector2 currentPos = hookRb.position;
        Vector2 newPos = Vector2.MoveTowards(
            currentPos,
            targetPosition,
            moveSpeed * Time.fixedDeltaTime
        );

        hookRb.MovePosition(newPos);

        // Update rope during both extending and retracting
        if (!retracting && _activeSegments < _numOfRopeSegments)
        {
            UpdateRopeSpawning();
        }
        else if (retracting)
        {
            UpdateRopeRetracting();
        }

        // HARD STOP
        if (Vector2.Distance(newPos, targetPosition) < 0.01f)
        {
            hookRb.MovePosition(targetPosition);
            moving = false;

            // If reached target but not grappled to valid surface, auto-retract
            if (!retracting && !isGrappled)
            {
                StartRetract();
            }
            // Destroy rope when retract completes
            else if (retracting)
            {
                DestroyRope();
                retracting = false;
                isGrappled = false;
                isPullingPlayer = false;
            }
            // Start pulling player when hook is grappled
            else if (isGrappled)
            {
                isPullingPlayer = true;

                // Disable player movement script during pull if enabled
                if (disablePlayerControlDuringPull && playerMovementScript != null)
                {
                    playerMovementScript.enabled = false;
                    Debug.Log("Disabled player control for grapple pull");
                }

                Debug.Log("Starting to pull player!");
            }
        }
    }

    private void UpdateRopeSpawning()
    {
        float distanceTraveled = Vector2.Distance((Vector2)playerTransform.position, hookRb.position);
        int targetSegments = Mathf.Min(
            Mathf.FloorToInt(distanceTraveled / _ropeSegmentLength),
            _numOfRopeSegments
        );

        // Add new segments as hook extends
        while (_activeSegments < targetSegments)
        {
            Vector2 spawnPos;
            if (_ropeSegments.Count == 0)
            {
                spawnPos = playerTransform.position;
            }
            else
            {
                Vector2 lastPos = _ropeSegments[_ropeSegments.Count - 1].CurrentPosition;
                Vector2 directionToHook = (hookRb.position - lastPos).normalized;
                spawnPos = lastPos + directionToHook * _ropeSegmentLength;
            }

            _ropeSegments.Add(new RopeSegment(spawnPos));
            _activeSegments++;
        }

        _lineRenderer.positionCount = _activeSegments;
    }

    private void UpdateRopeRetracting()
    {
        // Keep rope stretched between player and hook as it retracts
        float distanceTraveled = Vector2.Distance((Vector2)playerTransform.position, hookRb.position);
        int targetSegments = Mathf.Max(
            Mathf.FloorToInt(distanceTraveled / _ropeSegmentLength),
            2 // Minimum 2 segments to show rope
        );

        // Remove segments from the hook end as it gets closer
        while (_activeSegments > targetSegments && _activeSegments > 2)
        {
            _ropeSegments.RemoveAt(_ropeSegments.Count - 1);
            _activeSegments--;
        }

        _lineRenderer.positionCount = _activeSegments;
    }

    private void DestroyRope()
    {
        _ropeSegments.Clear();
        _activeSegments = 0;
        _lineRenderer.positionCount = 0;
    }

    private void ApplyRopeConstraints()
    {
        if (_activeSegments == 0) return;

        // First segment anchored to player
        RopeSegment firstSegment = _ropeSegments[0];
        firstSegment.CurrentPosition = playerTransform.position;
        _ropeSegments[0] = firstSegment;

        // Last segment anchored to hook
        if (_activeSegments > 1)
        {
            RopeSegment lastSegment = _ropeSegments[_activeSegments - 1];
            lastSegment.CurrentPosition = hookRb.position;
            _ropeSegments[_activeSegments - 1] = lastSegment;
        }

        // Apply distance constraints to keep rope taut
        for (int i = 0; i < _activeSegments - 1; i++)
        {
            RopeSegment currentSeg = _ropeSegments[i];
            RopeSegment nextSeg = _ropeSegments[i + 1];

            float dist = (currentSeg.CurrentPosition - nextSeg.CurrentPosition).magnitude;
            float difference = dist - _ropeSegmentLength;

            Vector2 changeDir = (currentSeg.CurrentPosition - nextSeg.CurrentPosition).normalized;
            Vector2 changeVector = changeDir * difference * _constraintStiffness;

            // Don't move first and last segments (anchored)
            if (i == 0)
            {
                nextSeg.CurrentPosition += changeVector;
            }
            else if (i == _activeSegments - 2)
            {
                currentSeg.CurrentPosition -= changeVector;
            }
            else
            {
                currentSeg.CurrentPosition -= changeVector * 0.5f;
                nextSeg.CurrentPosition += changeVector * 0.5f;
            }

            _ropeSegments[i] = currentSeg;
            _ropeSegments[i + 1] = nextSeg;
        }
    }

    private void HandleRopeCollisions()
    {
        // Skip first and last segments (anchored)
        for (int i = 1; i < _activeSegments - 1; i++)
        {
            RopeSegment segment = _ropeSegments[i];

            Collider2D[] colliders = Physics2D.OverlapCircleAll(
                segment.CurrentPosition,
                _collisionRadius,
                _collisionMask
            );

            foreach (Collider2D collider in colliders)
            {
                Vector2 closestPoint = collider.ClosestPoint(segment.CurrentPosition);
                float distance = Vector2.Distance(segment.CurrentPosition, closestPoint);

                if (distance < _collisionRadius)
                {
                    Vector2 normal = (segment.CurrentPosition - closestPoint).normalized;

                    if (normal == Vector2.zero)
                    {
                        normal = (segment.CurrentPosition - (Vector2)collider.transform.position).normalized;
                    }

                    float depth = _collisionRadius - distance;
                    segment.CurrentPosition += normal * depth;
                }
            }

            _ropeSegments[i] = segment;
        }
    }

    private void DrawRope()
    {
        if (_activeSegments == 0) return;

        Vector3[] ropePositions = new Vector3[_activeSegments];
        for (int i = 0; i < _activeSegments; i++)
        {
            ropePositions[i] = _ropeSegments[i].CurrentPosition;
        }

        _lineRenderer.SetPositions(ropePositions);
    }

    private void StartRetract()
    {
        targetPosition = playerTransform.position;
        retracting = true;
        moving = true;
        isPullingPlayer = false;

        // Re-enable player control
        if (disablePlayerControlDuringPull && playerMovementScript != null)
        {
            playerMovementScript.enabled = true;
            Debug.Log("Re-enabled player control");
        }
    }

    private void PullPlayerTowardsHook()
    {
        Vector2 playerPos = playerRb.position;
        Vector2 hookPos = hookRb.position;
        Vector2 directionToHook = (hookPos - playerPos).normalized;
        float distanceToHook = Vector2.Distance(playerPos, hookPos);

        // Stop pulling when player gets very close to hook
        if (distanceToHook < 1f)
        {
            isPullingPlayer = false;
            return;
        }

        // Apply force towards hook
        if (maintainMomentum)
        {
            // Add force to existing momentum (swinging feel)
            playerRb.AddForce(directionToHook * pullForce, ForceMode2D.Force);
        }
        else
        {
            // Direct pull (more controlled, less physics-based)
            Vector2 targetVelocity = directionToHook * pullSpeed;
            playerRb.velocity = Vector2.Lerp(playerRb.velocity, targetVelocity, 0.1f);
        }
    }

    private void AdjustRopeWhilePulling()
    {
        // Adjust rope segments as player moves closer to hook
        float distanceToHook = Vector2.Distance(playerTransform.position, hookRb.position);
        int targetSegments = Mathf.Max(
            Mathf.FloorToInt(distanceToHook / _ropeSegmentLength),
            2 // Minimum 2 segments
        );

        // Remove segments as player gets closer
        while (_activeSegments > targetSegments && _activeSegments > 2)
        {
            _ropeSegments.RemoveAt(0); // Remove from player end
            _activeSegments--;
        }

        _lineRenderer.positionCount = _activeSegments;
    }

    // Collision detection for grapple points
    private void OnTriggerEnter2D(Collider2D other)
    {
        // Check if hook hit a valid grapple surface
        if (((1 << other.gameObject.layer) & grappleMask) != 0 || other.CompareTag(grappleTag))
        {
            isGrappled = true;
            Debug.Log("Hook grappled to: " + other.gameObject.name);
        }
        else if (!retracting)
        {
            // Hit invalid surface while extending - auto retract
            isGrappled = false;
            Debug.Log("Hook hit invalid surface: " + other.gameObject.name + ", retracting...");
            StartRetract();
        }
    }

    void Update()
    {
        // Draw rope every frame
        DrawRope();

        // LEFT CLICK to Shoot
        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            // Only allow shooting if not currently grappled or retracting
            if (!isGrappled && !retracting)
            {
                Vector2 mousePos = Mouse.current.position.ReadValue();
                Vector2 clickedPos = mainCamera.ScreenToWorldPoint(mousePos);
                Vector2 dir = clickedPos - (Vector2)playerTransform.position;

                if (dir.magnitude > maxLineLength)
                    clickedPos = (Vector2)playerTransform.position + dir.normalized * maxLineLength;

                targetPosition = clickedPos;
                moving = true;
                isGrappled = false; // Reset grapple state
                isPullingPlayer = false; // Reset pull state

                // Clear previous rope
                DestroyRope();
            }
        }

        // RIGHT CLICK to Retract (or release while being pulled)
        if (Mouse.current.rightButton.wasPressedThisFrame)
        {
            if (!retracting)
            {
                StartRetract();
            }
        }

        // DEBUG: Press SPACE to manually test pull force
        if (Keyboard.current.spaceKey.wasPressedThisFrame && playerRb != null)
        {
            Vector2 testForce = Vector2.up * 100f;
            playerRb.AddForce(testForce, ForceMode2D.Force);
            Debug.Log($"DEBUG: Applied test force {testForce} - Current velocity: {playerRb.linearVelocity}");
        }
    }
}