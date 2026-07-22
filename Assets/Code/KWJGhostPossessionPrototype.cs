using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// One-file, runtime-built prototype for KWJ_3.
/// Drag from the possessed body and release to fire the ghost toward another body.
/// </summary>
public sealed class KWJGhostPossessionPrototype : MonoBehaviour
{
    private enum HostType { Human, Doll, Robot, Cat, Brute }
    private enum LaunchMode { Arc, Straight }
    private enum PickupType { Cloud, Shield }

    private sealed class Floor
    {
        public float y;
        public float left;
        public float right;
        public bool leftWall;
        public bool rightWall;
        public GameObject view;
    }

    private sealed class Host
    {
        public HostType type;
        public Floor floor;
        public GameObject view;
        public SpriteRenderer body;
        public SpriteRenderer aura;
        public float x;
        public float direction;
        public float verticalOffset;
        public float verticalVelocity;
        public bool falling;
        public bool possessed;
        public float phase;
        public float horizontalVelocity;
        public float impact;
    }

    private sealed class Enemy
    {
        public Floor floor;
        public GameObject view;
        public GameObject warning;
        public float x;
        public float warningTimer;
        public float phase;
        public bool alerted;
    }

    private sealed class SpringPad { public Floor floor; public float x; public GameObject view; }
    private sealed class SpeedPad { public Floor floor; public float x; public GameObject view; }
    private sealed class Fan { public Vector2 position; public Vector2 direction; public GameObject view; }
    private sealed class Pickup { public PickupType type; public Vector2 position; public GameObject view; public bool used; }
    private sealed class Shooter
    {
        public Floor floor;
        public float x;
        public GameObject view;
        public GameObject warning;
        public GameObject aimLine;
        public float warningTimer;
        public float cooldown;
        public Vector2 lockedTarget;
        public bool aiming;
    }
    private sealed class Bullet { public Vector2 position; public Vector2 velocity; public GameObject view; public float life; }

    [Header("Fast tuning")]
    [SerializeField] private float humanSpeed = 3.35f;
    [SerializeField] private float robotSpeed = 2.65f;
    [SerializeField] private float catSpeed = 2.8f;
    [SerializeField] private float catBurstSpeed = 7.2f;
    [SerializeField] private float bruteSpeed = 1.65f;
    [SerializeField] private float robotJumpPower = 8.6f;
    [SerializeField] private float ghostSpeed = 18f;
    [SerializeField] private float minimumGhostSpeed = 10f;
    [SerializeField] private float ghostGravity = 8.2f;
    [SerializeField] private float launchSpeedMultiplier = 1f;
    [SerializeField] private float maximumDragPixels = 280f;
    [SerializeField] private float minimumDragPixels = 24f;

    private readonly List<Floor> floors = new List<Floor>();
    private readonly List<Host> hosts = new List<Host>();
    private readonly List<GameObject> trail = new List<GameObject>();
    private readonly List<Enemy> enemies = new List<Enemy>();
    private readonly List<SpringPad> springs = new List<SpringPad>();
    private readonly List<SpeedPad> speedPads = new List<SpeedPad>();
    private readonly List<Fan> fans = new List<Fan>();
    private readonly List<Pickup> pickups = new List<Pickup>();
    private readonly List<Shooter> shooters = new List<Shooter>();
    private readonly List<Bullet> bullets = new List<Bullet>();

    private Camera gameCamera;
    private GameObject worldRoot;
    private Texture2D pixelTexture;
    private Sprite pixelSprite;
    private Host currentHost;
    private Host launchedFromHost;
    private Host lastPossessedHost;
    private GameObject ghost;
    private SpriteRenderer ghostRenderer;
    private Vector2 ghostPosition;
    private Vector2 ghostVelocity;
    private bool ghostFlying;
    private bool dragging;
    private bool dead;
    private bool won;
    private Vector2 dragStartScreen;
    private Vector2 dragCurrentScreen;
    private float currentAimPower;
    private float highestY;
    private float cameraY;
    private float cameraX;
    private float messageTimer;
    private string message = "DRAG TO ANOTHER BODY";
    private float flightTime;
    private int possessions;
    private int nextFloorIndex;
    private float lastGeneratedY;
    private float cameraKick;
    private float generatedCenterX;
    private LaunchMode launchMode = LaunchMode.Arc;
    private bool hasAirDash;
    private bool hasShield;
    private float springCooldown;

    private GUIStyle titleStyle;
    private GUIStyle hudStyle;
    private GUIStyle centerStyle;

    private const float FloorGap = 2.4f;
    private const float HostGroundOffset = 0.5f;

    private void Awake()
    {
        Application.targetFrameRate = 120;
        QualitySettings.vSyncCount = 0;

        pixelTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        pixelTexture.SetPixel(0, 0, Color.white);
        pixelTexture.Apply();
        pixelSprite = Sprite.Create(pixelTexture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);

        gameCamera = Camera.main;
        if (gameCamera == null)
        {
            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.AddComponent<AudioListener>();
            gameCamera = cameraObject.AddComponent<Camera>();
        }

        gameCamera.orthographic = true;
        // Zoomed out so several possible routes and possession targets are visible at once.
        gameCamera.orthographicSize = 8.6f;
        gameCamera.backgroundColor = new Color(0.055f, 0.075f, 0.13f);
        BuildGame();
    }

    private void BuildGame()
    {
        if (worldRoot != null) Destroy(worldRoot);
        floors.Clear();
        hosts.Clear();
        trail.Clear();
        enemies.Clear();
        springs.Clear();
        speedPads.Clear();
        fans.Clear();
        pickups.Clear();
        shooters.Clear();
        bullets.Clear();
        currentHost = null;
        lastPossessedHost = null;
        dead = false;
        won = false;
        dragging = false;
        ghostFlying = false;
        possessions = 0;
        nextFloorIndex = 0;
        lastGeneratedY = 0f;
        cameraKick = 0f;
        cameraX = 0f;
        generatedCenterX = 0f;
        hasAirDash = false;
        hasShield = false;
        springCooldown = 0f;
        highestY = 0f;
        cameraY = 2.4f;
        message = "DRAG TO ANOTHER BODY";
        messageTimer = 3f;
        worldRoot = new GameObject("KWJ_3 Runtime Prototype");

        BuildBackdrop();
        BuildLevel();
        CreateGhost();

        Host start = hosts[0];
        Possess(start);
        gameCamera.transform.position = new Vector3(0f, cameraY, -10f);
    }

    private void BuildBackdrop()
    {
        CreateBox("Night", new Vector2(0f, 250f), new Vector2(100f, 520f),
            new Color(0.045f, 0.06f, 0.115f), -50, worldRoot.transform);

        Random.InitState(3017);
        for (int i = 0; i < 260; i++)
        {
            float x = Random.Range(-45f, 45f);
            float y = Random.Range(-2f, 500f);
            float size = Random.Range(0.025f, 0.075f);
            CreateBox("Star", new Vector2(x, y), new Vector2(size, size),
                new Color(0.58f, 0.78f, 1f, Random.Range(0.3f, 0.85f)), -40, worldRoot.transform);
        }

        CreateBox("Moon", new Vector2(8f, 24f), new Vector2(2.2f, 2.2f),
            new Color(1f, 0.88f, 0.5f), -38, worldRoot.transform);
    }

    private void BuildLevel()
    {
        // Dense High Risers-like floors are extended upward during play.
        Random.InitState(7331);
        for (int i = 0; i < 11; i++) GenerateNextFloor();
    }

    private void GenerateNextFloor()
    {
        int i = nextFloorIndex++;
        float y = i * FloorGap;

        if (i == 0)
        {
            AddFloor(y, -5.5f, 4.5f, true, false, HostType.Doll, -2.8f, 1f);
            lastGeneratedY = y;
            return;
        }

        float horizontalStep = Random.Range(-3.3f, 3.3f);
        if (Mathf.Abs(horizontalStep) < 1.15f)
            horizontalStep = (i % 2 == 0 ? 1f : -1f) * 1.15f;
        generatedCenterX = Mathf.Clamp(generatedCenterX + horizontalStep, -20f, 20f);

        // Two islands on the same height create true horizontal routes.
        if (i % 5 == 2)
        {
            float gapCenter = generatedCenterX;
            float gapHalf = Random.Range(0.65f, 1.2f);
            float leftEnd = gapCenter - gapHalf;
            float rightStart = gapCenter + gapHalf;
            float leftStart = leftEnd - Random.Range(4.8f, 7.8f);
            float rightEnd = rightStart + Random.Range(4.8f, 7.8f);
            HostType leftType = (HostType)((i + 1) % 5);
            HostType rightType = (HostType)((i + 3) % 5);
            AddFloor(y, leftStart, leftEnd, true, false, leftType,
                Random.Range(leftStart + 0.75f, leftEnd - 0.75f), 1f);
            AddFloor(y, rightStart, rightEnd, false, true, rightType,
                Random.Range(rightStart + 0.75f, rightEnd - 0.75f), -1f, i % 10 != 7);
            lastGeneratedY = y;
            return;
        }

        // Long platforms wander freely in X instead of being packed in one vertical rectangle.
        float center = generatedCenterX;
        float width = Random.Range(6.2f, 11.5f);
        float left = center - width * 0.5f;
        float right = left + width;
        bool leftWall = i % 2 == 0;
        bool rightWall = !leftWall;

        // Occasional safe full-width floor resets the route before it branches again.
        if (i % 9 == 0)
        {
            left = center - 6.5f;
            right = center + 6.5f;
            leftWall = true;
            rightWall = true;
        }

        HostType type = (HostType)((i + 1) % 5);
        float hostX = Random.Range(left + 0.8f, right - 0.8f);
        float direction = Random.value < 0.5f ? -1f : 1f;
        AddFloor(y, left, right, leftWall, rightWall, type, hostX, direction, i % 4 != 0);
        lastGeneratedY = y;
    }

    private void ExtendLevelIfNeeded()
    {
        float playerY = currentHost != null ? currentHost.floor.y : ghostPosition.y;
        float wantedTop = Mathf.Max(highestY, playerY) + 17f;
        while (lastGeneratedY < wantedTop) GenerateNextFloor();
    }

    private void AddFloor(float y, float left, float right, bool leftWall, bool rightWall,
        HostType type, float hostX, float direction, bool spawnHost = true)
    {
        Floor floor = new Floor
        {
            y = y,
            left = left,
            right = right,
            leftWall = leftWall,
            rightWall = rightWall
        };

        GameObject group = new GameObject("Floor " + y);
        group.transform.SetParent(worldRoot.transform);
        floor.view = group;
        float width = right - left;
        int floorNumber = floors.Count;
        Color floorColor = Color.Lerp(new Color(0.18f, 0.26f, 0.44f),
            new Color(0.31f, 0.2f, 0.48f), Mathf.PingPong(floorNumber * 0.16f, 1f));
        CreateBox("Platform", new Vector2((left + right) * 0.5f, y), new Vector2(width, 0.28f),
            floorColor, 1, group.transform);
        CreateBox("Platform Light", new Vector2((left + right) * 0.5f, y + 0.12f), new Vector2(width, 0.055f),
            new Color(0.25f, 0.85f, 1f, 0.8f), 2, group.transform);

        if (leftWall)
            CreateBox("Left Wall", new Vector2(left + 0.13f, y + 1.2f), new Vector2(0.26f, 2.4f),
                new Color(0.18f, 0.25f, 0.39f), 1, group.transform);
        else
            CreateEdgeWarning(new Vector2(left, y + 0.22f), group.transform);

        if (rightWall)
            CreateBox("Right Wall", new Vector2(right - 0.13f, y + 1.2f), new Vector2(0.26f, 2.4f),
                new Color(0.18f, 0.25f, 0.39f), 1, group.transform);
        else
            CreateEdgeWarning(new Vector2(right, y + 0.22f), group.transform);

        // Building windows make newly generated floors visibly stream past the camera.
        for (float windowX = left + 0.65f; windowX <= right - 0.65f; windowX += 1.35f)
        {
            Color windowColor = ((Mathf.RoundToInt(windowX * 10f) + floorNumber) & 1) == 0
                ? new Color(1f, 0.72f, 0.22f, 0.5f)
                : new Color(0.16f, 0.55f, 0.75f, 0.28f);
            CreateBox("Window", new Vector2(windowX, y - 0.72f), new Vector2(0.48f, 0.62f),
                windowColor, -2, group.transform);
        }

        floors.Add(floor);
        if (spawnHost) CreateHost(type, floor, hostX, direction);
        if (floorNumber >= 4 && floorNumber % 8 == 5) CreateEnemy(floor);
        if (floorNumber >= 2 && floorNumber % 7 == 2) CreateSpring(floor);
        if (floorNumber >= 3 && floorNumber % 7 == 4) CreateSpeedPad(floor);
        if (floorNumber >= 4 && floorNumber % 9 == 6) CreateFan(floor, floorNumber);
        if (floorNumber >= 3 && floorNumber % 11 == 3)
            CreatePickup(floor, (floorNumber / 11) % 2 == 0 ? PickupType.Cloud : PickupType.Shield);
        if (floorNumber >= 6 && floorNumber % 10 == 7) CreateShooter(floor);
    }

    private void CreateEdgeWarning(Vector2 position, Transform parent)
    {
        GameObject warning = CreateBox("Open Edge", position, new Vector2(0.12f, 0.45f),
            new Color(1f, 0.2f, 0.28f), 4, parent);
        warning.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
    }

    private void CreateHost(HostType type, Floor floor, float x, float direction)
    {
        GameObject view = new GameObject(type.ToString());
        view.transform.SetParent(worldRoot.transform);
        SpriteRenderer aura = CreateBox("Possession Aura", Vector2.zero, new Vector2(1.12f, 1.45f),
            new Color(0.4f, 1f, 0.92f, 0f), 9, view.transform).GetComponent<SpriteRenderer>();
        SpriteRenderer body = CreateBox("Body", Vector2.zero, new Vector2(0.68f, 1.02f),
            HostColor(type), 10, view.transform).GetComponent<SpriteRenderer>();

        if (type == HostType.Human)
        {
            CreateBox("Head", new Vector2(0f, 0.62f), new Vector2(0.46f, 0.46f),
                new Color(1f, 0.76f, 0.57f), 11, view.transform);
            CreateBox("Leg L", new Vector2(-0.19f, -0.62f), new Vector2(0.18f, 0.45f),
                new Color(0.2f, 0.26f, 0.4f), 10, view.transform);
            CreateBox("Leg R", new Vector2(0.19f, -0.62f), new Vector2(0.18f, 0.45f),
                new Color(0.2f, 0.26f, 0.4f), 10, view.transform);
        }
        else if (type == HostType.Doll)
        {
            CreateBox("Doll Head", new Vector2(0f, 0.61f), new Vector2(0.58f, 0.5f),
                new Color(1f, 0.72f, 0.78f), 11, view.transform);
            CreateBox("Bow", new Vector2(0.34f, 0.8f), new Vector2(0.3f, 0.18f),
                new Color(1f, 0.18f, 0.42f), 12, view.transform);
        }
        else if (type == HostType.Robot)
        {
            CreateBox("Robot Head", new Vector2(0f, 0.58f), new Vector2(0.62f, 0.46f),
                new Color(0.7f, 0.84f, 0.92f), 11, view.transform);
            CreateBox("Robot Eye", new Vector2(0.16f, 0.6f), new Vector2(0.13f, 0.13f),
                new Color(1f, 0.24f, 0.18f), 12, view.transform);
            CreateBox("Antenna", new Vector2(0f, 0.91f), new Vector2(0.08f, 0.28f),
                new Color(1f, 0.72f, 0.18f), 11, view.transform);
        }
        else if (type == HostType.Cat)
        {
            body.transform.localScale = new Vector3(0.95f, 0.58f, 1f);
            body.transform.localPosition = new Vector3(0f, -0.18f, 0f);
            CreateBox("Cat Head", new Vector2(0.3f, 0.12f), new Vector2(0.48f, 0.46f),
                new Color(0.96f, 0.78f, 0.3f), 11, view.transform);
            GameObject earL = CreateBox("Ear L", new Vector2(0.18f, 0.4f), new Vector2(0.2f, 0.24f),
                new Color(0.9f, 0.55f, 0.18f), 11, view.transform);
            GameObject earR = CreateBox("Ear R", new Vector2(0.43f, 0.4f), new Vector2(0.2f, 0.24f),
                new Color(0.9f, 0.55f, 0.18f), 11, view.transform);
            earL.transform.localRotation = Quaternion.Euler(0f, 0f, -20f);
            earR.transform.localRotation = Quaternion.Euler(0f, 0f, 20f);
            CreateBox("Tail", new Vector2(-0.48f, 0.02f), new Vector2(0.52f, 0.13f),
                new Color(0.9f, 0.55f, 0.18f), 10, view.transform).transform.localRotation = Quaternion.Euler(0f, 0f, 32f);
        }
        else
        {
            body.transform.localScale = new Vector3(1.38f, 1.3f, 1f);
            CreateBox("Brute Head", new Vector2(0f, 0.78f), new Vector2(0.72f, 0.62f),
                new Color(0.58f, 0.82f, 0.48f), 11, view.transform);
            CreateBox("Shoulder L", new Vector2(-0.55f, 0.18f), new Vector2(0.42f, 0.56f),
                new Color(0.27f, 0.46f, 0.25f), 10, view.transform);
            CreateBox("Shoulder R", new Vector2(0.55f, 0.18f), new Vector2(0.42f, 0.56f),
                new Color(0.27f, 0.46f, 0.25f), 10, view.transform);
        }

        Host host = new Host
        {
            type = type,
            floor = floor,
            view = view,
            body = body,
            aura = aura,
            x = x,
            direction = direction,
            phase = Random.Range(0f, 3f)
        };
        hosts.Add(host);
        UpdateHostTransform(host);
    }

    private void CreateEnemy(Floor floor)
    {
        Enemy enemy = new Enemy
        {
            floor = floor,
            x = Mathf.Lerp(floor.left, floor.right, Random.Range(0.28f, 0.72f)),
            phase = Random.Range(0f, 5f)
        };

        enemy.view = new GameObject("RED HUNTER");
        enemy.view.transform.SetParent(worldRoot.transform);
        CreateBox("Enemy Body", Vector2.zero, new Vector2(0.72f, 1.08f),
            new Color(0.82f, 0.04f, 0.06f), 13, enemy.view.transform);
        CreateBox("Enemy Head", new Vector2(0f, 0.66f), new Vector2(0.5f, 0.5f),
            new Color(1f, 0.18f, 0.16f), 14, enemy.view.transform);
        CreateBox("Enemy Leg L", new Vector2(-0.2f, -0.65f), new Vector2(0.2f, 0.46f),
            new Color(0.28f, 0.01f, 0.02f), 13, enemy.view.transform);
        CreateBox("Enemy Leg R", new Vector2(0.2f, -0.65f), new Vector2(0.2f, 0.46f),
            new Color(0.28f, 0.01f, 0.02f), 13, enemy.view.transform);

        enemy.warning = new GameObject("RED EXCLAMATION");
        enemy.warning.transform.SetParent(enemy.view.transform);
        CreateBox("Warning Glow", Vector2.zero, new Vector2(0.58f, 0.82f),
            new Color(1f, 0.86f, 0.18f), 18, enemy.warning.transform);
        CreateBox("!", new Vector2(0f, 0.08f), new Vector2(0.13f, 0.42f),
            new Color(0.8f, 0.01f, 0.02f), 19, enemy.warning.transform);
        CreateBox("! Dot", new Vector2(0f, -0.24f), new Vector2(0.14f, 0.14f),
            new Color(0.8f, 0.01f, 0.02f), 19, enemy.warning.transform);
        enemy.warning.transform.localPosition = new Vector3(0f, 1.48f, 0f);
        enemy.warning.SetActive(false);
        enemy.view.transform.position = new Vector3(enemy.x, floor.y + HostGroundOffset, 0f);
        enemy.view.transform.localScale = new Vector3(0.72f, 0.72f, 1f);
        enemies.Add(enemy);
    }

    private void UpdateEnemies(float dt)
    {
        for (int i = 0; i < enemies.Count; i++)
        {
            Enemy enemy = enemies[i];
            bool playerOnFloor = currentHost != null && !currentHost.falling && currentHost.floor == enemy.floor;
            if (playerOnFloor)
            {
                if (!enemy.alerted)
                {
                    enemy.alerted = true;
                    enemy.warningTimer = 0.9f;
                }

                enemy.warning.SetActive(true);
                float pulse = 1f + Mathf.Sin(Time.unscaledTime * 18f) * 0.15f;
                enemy.warning.transform.localScale = new Vector3(pulse, pulse, 1f);
                if (enemy.warningTimer > 0f)
                    enemy.warningTimer -= dt;
                else
                {
                    float targetX = currentHost.x;
                    float direction = Mathf.Sign(targetX - enemy.x);
                    enemy.x = Mathf.MoveTowards(enemy.x, targetX, 5.8f * dt);
                    enemy.view.transform.localRotation = Quaternion.Euler(0f, direction < 0f ? 180f : 0f,
                        Mathf.Sin(Time.unscaledTime * 16f) * 5f);
                    if (Mathf.Abs(enemy.x - targetX) < 0.62f)
                    {
                        Die("THE RED HUNTER GOT YOU");
                        return;
                    }
                }
            }
            else
            {
                enemy.alerted = false;
                enemy.warning.SetActive(false);
                enemy.view.transform.localRotation = Quaternion.Euler(0f, 0f,
                    Mathf.Sin(Time.unscaledTime * 2.4f + enemy.phase) * 2f);
            }

            float bob = Mathf.Abs(Mathf.Sin(Time.unscaledTime * (enemy.alerted ? 13f : 2.5f) + enemy.phase)) * 0.06f;
            enemy.view.transform.position = new Vector3(enemy.x, enemy.floor.y + HostGroundOffset + bob, 0f);
        }
    }

    private void CreateSpring(Floor floor)
    {
        float x = Random.Range(floor.left + 1f, floor.right - 1f);
        GameObject view = new GameObject("SPRING");
        view.transform.SetParent(worldRoot.transform);
        CreateBox("Spring Base", Vector2.zero, new Vector2(0.9f, 0.16f), new Color(0.2f, 0.9f, 0.45f), 8, view.transform);
        for (int i = 0; i < 3; i++)
            CreateBox("Coil", new Vector2(0f, 0.14f + i * 0.13f), new Vector2(0.55f - i * 0.1f, 0.08f),
                new Color(0.8f, 1f, 0.35f), 9, view.transform);
        view.transform.position = new Vector3(x, floor.y + 0.24f, 0f);
        springs.Add(new SpringPad { floor = floor, x = x, view = view });
    }

    private void CreateSpeedPad(Floor floor)
    {
        float x = Random.Range(floor.left + 1.1f, floor.right - 1.1f);
        GameObject view = CreateBox("SPEED PAD", new Vector2(x, floor.y + 0.2f), new Vector2(1.8f, 0.16f),
            new Color(0.15f, 0.75f, 1f), 8, worldRoot.transform);
        CreateBox("Speed Stripe", new Vector2(x, floor.y + 0.23f), new Vector2(0.7f, 0.06f),
            new Color(1f, 1f, 1f), 9, worldRoot.transform);
        speedPads.Add(new SpeedPad { floor = floor, x = x, view = view });
    }

    private void CreateFan(Floor floor, int index)
    {
        Vector2 direction = index % 2 == 0 ? Vector2.right : Vector2.left;
        Vector2 position = new Vector2((floor.left + floor.right) * 0.5f, floor.y + 1.45f);
        GameObject view = new GameObject("FAN WIND " + (direction.x > 0f ? "RIGHT" : "LEFT"));
        view.transform.SetParent(worldRoot.transform);
        CreateBox("Fan Hub", Vector2.zero, new Vector2(0.34f, 0.34f), new Color(0.65f, 0.8f, 0.9f), 7, view.transform);
        for (int i = 0; i < 4; i++)
        {
            GameObject blade = CreateBox("Blade", Vector2.zero, new Vector2(0.18f, 1.05f),
                new Color(0.35f, 0.85f, 1f, 0.8f), 6, view.transform);
            blade.transform.localRotation = Quaternion.Euler(0f, 0f, i * 90f);
        }
        view.transform.position = position;
        CreateBox("Wind Arrow", position + direction * 1.25f, new Vector2(1.2f, 0.1f),
            new Color(0.45f, 0.9f, 1f, 0.5f), 5, worldRoot.transform);
        fans.Add(new Fan { position = position, direction = direction, view = view });
    }

    private void CreatePickup(Floor floor, PickupType type)
    {
        Vector2 position = new Vector2(Random.Range(floor.left + 0.9f, floor.right - 0.9f), floor.y + 1.35f);
        Color color = type == PickupType.Cloud ? new Color(0.78f, 0.95f, 1f) : new Color(0.3f, 0.9f, 0.65f);
        GameObject view = CreateBox(type.ToString().ToUpper() + " ITEM", position, new Vector2(0.65f, 0.65f),
            color, 12, worldRoot.transform);
        if (type == PickupType.Cloud)
        {
            GameObject puffL = CreateBox("Cloud Puff L", position + new Vector2(-0.35f, 0f), new Vector2(0.45f, 0.4f), color, 12, worldRoot.transform);
            GameObject puffR = CreateBox("Cloud Puff R", position + new Vector2(0.35f, 0f), new Vector2(0.45f, 0.4f), color, 12, worldRoot.transform);
            puffL.transform.SetParent(view.transform, true);
            puffR.transform.SetParent(view.transform, true);
        }
        else
        {
            GameObject core = CreateBox("Shield Core", position, new Vector2(0.22f, 0.48f), Color.white, 13, worldRoot.transform);
            core.transform.SetParent(view.transform, true);
        }
        pickups.Add(new Pickup { type = type, position = position, view = view });
    }

    private void CreateShooter(Floor floor)
    {
        Shooter shooter = new Shooter
        {
            floor = floor,
            x = Mathf.Lerp(floor.left, floor.right, Random.Range(0.2f, 0.8f)),
            cooldown = 1f
        };
        shooter.view = new GameObject("RANGED ENEMY");
        shooter.view.transform.SetParent(worldRoot.transform);
        CreateBox("Shooter Body", Vector2.zero, new Vector2(0.68f, 1f), new Color(0.55f, 0.08f, 0.12f), 13, shooter.view.transform);
        CreateBox("Gun", new Vector2(0.48f, 0.12f), new Vector2(0.72f, 0.16f), new Color(0.12f, 0.12f, 0.16f), 14, shooter.view.transform);
        shooter.warning = CreateBox("Shooter !", Vector2.zero, new Vector2(0.48f, 0.7f),
            new Color(1f, 0.82f, 0.18f), 18, shooter.view.transform);
        CreateBox("! Mark", new Vector2(0f, 0.08f), new Vector2(0.11f, 0.34f),
            new Color(0.85f, 0.01f, 0.02f), 19, shooter.warning.transform);
        CreateBox("! Dot", new Vector2(0f, -0.2f), new Vector2(0.12f, 0.12f),
            new Color(0.85f, 0.01f, 0.02f), 19, shooter.warning.transform);
        shooter.warning.transform.localPosition = new Vector3(0f, 1.35f, 0f);
        shooter.warning.SetActive(false);
        shooter.aimLine = CreateBox("VISIBLE BULLET TRAJECTORY", Vector2.zero, Vector2.one,
            new Color(1f, 0.1f, 0.1f, 0.48f), 11, worldRoot.transform);
        shooter.aimLine.SetActive(false);
        shooter.view.transform.position = new Vector3(shooter.x, floor.y + HostGroundOffset, 0f);
        shooter.view.transform.localScale = new Vector3(0.72f, 0.72f, 1f);
        shooters.Add(shooter);
    }

    private void UpdateWorldObjects(float dt)
    {
        springCooldown = Mathf.Max(0f, springCooldown - dt);
        if (currentHost != null && !currentHost.falling && springCooldown <= 0f)
        {
            for (int i = 0; i < springs.Count; i++)
            {
                SpringPad spring = springs[i];
                if (currentHost.floor != spring.floor || Mathf.Abs(currentHost.x - spring.x) > 0.58f ||
                    Mathf.Abs(currentHost.verticalOffset) > 0.18f) continue;
                currentHost.falling = true;
                currentHost.verticalVelocity = 11.6f;
                currentHost.horizontalVelocity = currentHost.direction * Mathf.Max(2.2f, GetBaseSpeed(currentHost.type));
                springCooldown = 0.65f;
                cameraKick = 0.2f;
                message = "SPRING JUMP!";
                messageTimer = 0.55f;
                break;
            }
        }

        for (int i = 0; i < fans.Count; i++)
        {
            Fan fan = fans[i];
            fan.view.transform.Rotate(0f, 0f, 620f * dt);
            if (ghostFlying && Vector2.Distance(ghostPosition, fan.position) < 3.2f)
                ghostVelocity += fan.direction * 8.5f * dt;
        }

        if (ghostFlying)
        {
            for (int i = 0; i < pickups.Count; i++)
            {
                Pickup pickup = pickups[i];
                if (pickup.used || Vector2.Distance(ghostPosition, pickup.position) > 0.72f) continue;
                pickup.used = true;
                pickup.view.SetActive(false);
                if (pickup.type == PickupType.Cloud)
                {
                    hasAirDash = true;
                    message = "CLOUD: AIR DASH READY";
                }
                else
                {
                    hasShield = true;
                    message = "SHIELD: REVIVE READY";
                }
                messageTimer = 1.1f;
            }
        }

        UpdateShooters(dt);
        UpdateBullets(dt);
    }

    private float GetSpeedPadMultiplier(Host host)
    {
        if (!host.possessed || host.falling || Mathf.Abs(host.verticalOffset) > 0.2f) return 1f;
        for (int i = 0; i < speedPads.Count; i++)
            if (speedPads[i].floor == host.floor && Mathf.Abs(speedPads[i].x - host.x) < 1.05f)
                return 2.05f;
        return 1f;
    }

    private void UpdateShooters(float dt)
    {
        Vector2 playerPosition = ghostFlying ? ghostPosition : currentHost != null ? GetHostPosition(currentHost) : Vector2.zero;
        bool hasPlayer = ghostFlying || currentHost != null;
        for (int i = 0; i < shooters.Count; i++)
        {
            Shooter shooter = shooters[i];
            shooter.cooldown = Mathf.Max(0f, shooter.cooldown - dt);
            Vector2 muzzle = new Vector2(shooter.x, shooter.floor.y + 0.72f);
            if (!shooter.aiming && hasPlayer && shooter.cooldown <= 0f && Vector2.Distance(muzzle, playerPosition) < 9.5f)
            {
                shooter.aiming = true;
                shooter.warningTimer = 1.65f;
                shooter.warning.SetActive(true);
                shooter.aimLine.SetActive(true);
            }

            if (!shooter.aiming) continue;
            if (!hasPlayer || Vector2.Distance(muzzle, playerPosition) > 11.5f)
            {
                shooter.aiming = false;
                shooter.warning.SetActive(false);
                shooter.aimLine.SetActive(false);
                shooter.cooldown = 1.5f;
                continue;
            }

            shooter.lockedTarget = playerPosition;
            DrawWorldLine(shooter.aimLine, muzzle, shooter.lockedTarget, 0.055f);
            float pulse = 1f + Mathf.Sin(Time.unscaledTime * 20f) * 0.18f;
            shooter.warning.transform.localScale = new Vector3(pulse, pulse, 1f);
            shooter.warningTimer -= dt;
            if (shooter.warningTimer <= 0f)
            {
                Vector2 direction = (shooter.lockedTarget - muzzle).normalized;
                GameObject bulletView = CreateBox("ENEMY BULLET", muzzle, new Vector2(0.3f, 0.3f),
                    new Color(1f, 0.18f, 0.05f), 22, worldRoot.transform);
                bullets.Add(new Bullet { position = muzzle, velocity = direction * 12f, view = bulletView, life = 3f });
                shooter.aiming = false;
                shooter.warning.SetActive(false);
                shooter.aimLine.SetActive(false);
                shooter.cooldown = 3.2f;
            }
        }
    }

    private void UpdateBullets(float dt)
    {
        for (int i = bullets.Count - 1; i >= 0; i--)
        {
            Bullet bullet = bullets[i];
            bullet.life -= dt;
            bullet.position += bullet.velocity * dt;
            bullet.view.transform.position = bullet.position;
            Vector2 playerPosition = ghostFlying ? ghostPosition : currentHost != null ? GetHostPosition(currentHost) : new Vector2(9999f, 9999f);
            if (Vector2.Distance(bullet.position, playerPosition) < 0.52f)
            {
                Destroy(bullet.view);
                bullets.RemoveAt(i);
                Die("SHOT BY A RANGED ENEMY");
                return;
            }
            if (bullet.life <= 0f)
            {
                Destroy(bullet.view);
                bullets.RemoveAt(i);
            }
        }
    }

    private static void DrawWorldLine(GameObject line, Vector2 start, Vector2 end, float thickness)
    {
        Vector2 delta = end - start;
        line.transform.position = (start + end) * 0.5f;
        line.transform.localScale = new Vector3(delta.magnitude, thickness, 1f);
        line.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
    }

    private void CreateGhost()
    {
        ghost = new GameObject("GHOST PLAYER");
        ghost.transform.SetParent(worldRoot.transform);
        ghostRenderer = CreateBox("Ghost Glow", Vector2.zero, new Vector2(0.58f, 0.74f),
            new Color(0.38f, 1f, 0.9f), 20, ghost.transform).GetComponent<SpriteRenderer>();
        CreateBox("Eye L", new Vector2(-0.14f, 0.1f), new Vector2(0.09f, 0.13f),
            new Color(0.02f, 0.08f, 0.12f), 21, ghost.transform);
        CreateBox("Eye R", new Vector2(0.14f, 0.1f), new Vector2(0.09f, 0.13f),
            new Color(0.02f, 0.08f, 0.12f), 21, ghost.transform);
    }

    private void Update()
    {
        float dt = Mathf.Min(Time.deltaTime, 0.033f);
        if (messageTimer > 0f) messageTimer -= dt;
        if (Keyboard.current != null && Keyboard.current.qKey.wasPressedThisFrame)
            launchMode = launchMode == LaunchMode.Arc ? LaunchMode.Straight : LaunchMode.Arc;

        if (dead || won)
        {
            if (WasRestartPressed()) BuildGame();
            UpdateCamera(dt);
            return;
        }

        HandlePointer();
        // Other bodies keep moving while the player aims.
        UpdateHosts(dt);
        UpdateWorldObjects(dt);
        UpdateEnemies(dt);
        UpdateGhost(dt);
        ExtendLevelIfNeeded();
        UpdateCamera(dt);

        if (currentHost != null)
            highestY = Mathf.Max(highestY, currentHost.floor.y);
    }

    private void HandlePointer()
    {
        Pointer pointer = Pointer.current;
        bool canNormalLaunch = !ghostFlying && currentHost != null;
        bool canAirDash = ghostFlying && hasAirDash;
        if (pointer == null || (!canNormalLaunch && !canAirDash)) return;

        Vector2 screenPosition = pointer.position.ReadValue();
        if (pointer.press.wasPressedThisFrame)
        {
            if (IsOverLaunchModeUI(screenPosition)) return;
            // The gesture can begin anywhere on the screen, just like Ninja Tobu.
            dragging = true;
            dragStartScreen = screenPosition;
            dragCurrentScreen = screenPosition;
            currentAimPower = 0f;
        }
        else if (dragging && pointer.press.isPressed)
        {
            dragCurrentScreen = screenPosition;
            currentAimPower = Mathf.Clamp01((dragCurrentScreen - dragStartScreen).magnitude /
                Mathf.Max(maximumDragPixels, Screen.height * 0.28f));
        }
        else if (dragging && pointer.press.wasReleasedThisFrame)
        {
            dragCurrentScreen = screenPosition;
            Vector2 drag = dragCurrentScreen - dragStartScreen;
            currentAimPower = Mathf.Clamp01(drag.magnitude /
                Mathf.Max(maximumDragPixels, Screen.height * 0.28f));
            dragging = false;
            if (drag.magnitude >= minimumDragPixels)
            {
                if (ghostFlying) LaunchAirDash(drag.normalized, currentAimPower);
                else LaunchGhost(drag.normalized, currentAimPower);
            }
        }
    }

    private void LaunchAirDash(Vector2 direction, float power)
    {
        hasAirDash = false;
        flightTime = 0f;
        ghostVelocity = direction * Mathf.Lerp(minimumGhostSpeed, ghostSpeed, Mathf.Clamp01(power)) * launchSpeedMultiplier;
        message = "CLOUD DASH!";
        messageTimer = 0.55f;
    }

    private bool IsOverLaunchModeUI(Vector2 bottomLeftScreenPosition)
    {
        Vector2 guiPosition = new Vector2(bottomLeftScreenPosition.x, Screen.height - bottomLeftScreenPosition.y);
        Rect speed = GetSpeedSliderRect();
        Rect gravity = GetGravitySliderRect();
        speed.y -= 22f;
        speed.height += 32f;
        gravity.y -= 22f;
        gravity.height += 32f;
        return GetArcButtonRect().Contains(guiPosition) || GetStraightButtonRect().Contains(guiPosition) ||
            speed.Contains(guiPosition) || gravity.Contains(guiPosition);
    }

    private Rect GetArcButtonRect()
    {
        float margin = Mathf.Max(14f, Screen.width * 0.025f);
        float panelHeight = Mathf.Clamp(Screen.height * 0.075f, 58f, 90f);
        float width = Mathf.Clamp(Screen.width * 0.19f, 105f, 190f);
        return new Rect(margin, margin + panelHeight + 8f, width, 48f);
    }

    private Rect GetStraightButtonRect()
    {
        Rect arc = GetArcButtonRect();
        return new Rect(arc.xMax + 8f, arc.y, arc.width, arc.height);
    }

    private Rect GetSpeedSliderRect()
    {
        Rect arc = GetArcButtonRect();
        Rect straight = GetStraightButtonRect();
        return new Rect(arc.x, arc.yMax + 28f, straight.xMax - arc.x, 22f);
    }

    private Rect GetGravitySliderRect()
    {
        Rect speed = GetSpeedSliderRect();
        return new Rect(speed.x, speed.yMax + 30f, speed.width, 22f);
    }

    private void LaunchGhost(Vector2 direction, float power)
    {
        ghostPosition = GetHostPosition(currentHost) + Vector2.up * 0.1f;
        launchedFromHost = currentHost;
        currentHost.possessed = false;
        currentHost.aura.color = new Color(0.4f, 1f, 0.92f, 0f);
        currentHost = null;
        ghostFlying = true;
        flightTime = 0f;
        ghostVelocity = direction * Mathf.Lerp(minimumGhostSpeed, ghostSpeed, Mathf.Clamp01(power)) * launchSpeedMultiplier;
        ghost.transform.position = ghostPosition;
        ghost.SetActive(true);
        message = "FIND A BODY!";
        messageTimer = 0.8f;
    }

    private void UpdateGhost(float dt)
    {
        if (!ghostFlying) return;

        flightTime += dt;
        if (launchMode == LaunchMode.Arc)
            ghostVelocity += Vector2.down * ghostGravity * dt;
        ghostPosition += ghostVelocity * dt;
        ghost.transform.position = ghostPosition;
        ghost.transform.Rotate(0f, 0f, 240f * dt);

        if (Mathf.FloorToInt(flightTime * 16f) != Mathf.FloorToInt((flightTime - dt) * 16f))
        {
            GameObject puff = CreateBox("Ghost Trail", ghostPosition, new Vector2(0.18f, 0.18f),
                new Color(0.3f, 1f, 0.88f, 0.45f), 15, worldRoot.transform);
            trail.Add(puff);
            Destroy(puff, 0.35f);
        }

        for (int i = 0; i < hosts.Count; i++)
        {
            Host host = hosts[i];
            // Do not immediately collide with the body the ghost has just left.
            if (host == launchedFromHost && flightTime < 0.22f) continue;
            if (host.falling || Vector2.Distance(ghostPosition, GetHostPosition(host)) > 0.72f) continue;
            Possess(host);
            return;
        }

        // Missing one body is not an instant failure. The ghost can fall back into any
        // earlier or later body; only leaving the whole tower ends the run.
        if (ghostPosition.y < -6.5f || flightTime > 6.5f)
            Die("THE GHOST FADED AWAY");
    }

    private void Possess(Host host)
    {
        currentHost = host;
        lastPossessedHost = host;
        launchedFromHost = null;
        host.possessed = true;
        host.aura.color = new Color(0.28f, 1f, 0.82f, 0.5f);
        ghostFlying = false;
        ghost.SetActive(false);
        possessions++;
        host.impact = 1f;
        cameraKick = 0.2f;
        message = host.type == HostType.Human ? "HUMAN: FAST RUNNER" :
            host.type == HostType.Robot ? "ROBOT: HOPS BETWEEN FLOORS" :
            host.type == HostType.Cat ? "CAT: BURST SPEED" :
            host.type == HostType.Brute ? "BRUTE: SLOW AND HEAVY" : "DOLL: STAYS STILL";
        messageTimer = 1.2f;
    }

    private void UpdateHosts(float dt)
    {
        for (int i = 0; i < hosts.Count; i++)
        {
            Host host = hosts[i];
            if (host.falling)
            {
                float previousWorldY = GetHostPosition(host).y;
                host.verticalVelocity -= 14f * dt;
                host.x += host.horizontalVelocity * dt;
                host.verticalOffset += host.verticalVelocity * dt;
                float currentWorldY = GetHostPosition(host).y;

                Floor landingFloor = FindLandingFloor(host, previousWorldY, currentWorldY, false);
                if (landingFloor != null)
                {
                    LandHost(host, landingFloor);
                    UpdateHostTransform(host);
                    continue;
                }

                host.view.transform.position = GetHostPosition(host);
                host.view.transform.Rotate(0f, 0f, host.direction * -150f * dt);
                if (host.possessed && currentWorldY < -6.5f)
                    Die("YOU FELL WITH THE BODY");
                continue;
            }

            host.impact = Mathf.MoveTowards(host.impact, 0f, 5f * dt);
            float heightSpeed = 1f + Mathf.Min(host.floor.y, 180f) * 0.0035f;
            heightSpeed *= GetSpeedPadMultiplier(host);
            if (host.type == HostType.Human)
                MoveWalkingHost(host, humanSpeed * heightSpeed, dt);
            else if (host.type == HostType.Cat)
            {
                bool bursting = Mathf.Repeat(Time.unscaledTime + host.phase, 2.6f) < 0.62f;
                MoveWalkingHost(host, (bursting ? catBurstSpeed : catSpeed) * heightSpeed, dt);
            }
            else if (host.type == HostType.Brute)
                MoveWalkingHost(host, bruteSpeed * heightSpeed, dt);
            else if (host.type == HostType.Robot)
            {
                float previousWorldY = GetHostPosition(host).y;
                host.verticalVelocity -= 14f * dt;
                host.verticalOffset += host.verticalVelocity * dt;
                float currentWorldY = GetHostPosition(host).y;
                Floor landingFloor = FindLandingFloor(host, previousWorldY, currentWorldY, true);
                if (landingFloor != null)
                {
                    LandHost(host, landingFloor);
                }
                MoveWalkingHost(host, robotSpeed * heightSpeed, dt);
            }

            UpdateHostTransform(host);
            float pulse = host.possessed ? 0.42f + Mathf.Sin(Time.unscaledTime * 8f) * 0.15f : 0f;
            Color auraColor = host.aura.color;
            auraColor.a = pulse;
            host.aura.color = auraColor;
        }
    }

    private void MoveWalkingHost(Host host, float speed, float dt)
    {
        float wantedVelocity = host.direction * speed;
        host.horizontalVelocity = Mathf.MoveTowards(host.horizontalVelocity, wantedVelocity, 12f * dt);
        float nextX = host.x + host.horizontalVelocity * dt;
        float halfWidth = 0.38f;
        float leftLimit = host.floor.left + halfWidth;
        float rightLimit = host.floor.right - halfWidth;

        if (host.direction < 0f && nextX <= leftLimit)
        {
            if (host.floor.leftWall || !host.possessed)
            {
                host.x = leftLimit;
                host.direction = 1f;
                host.horizontalVelocity = Mathf.Abs(host.horizontalVelocity) * 0.72f;
                host.impact = 1f;
            }
            else
            {
                host.x = nextX;
                StartFalling(host);
            }
        }
        else if (host.direction > 0f && nextX >= rightLimit)
        {
            if (host.floor.rightWall || !host.possessed)
            {
                host.x = rightLimit;
                host.direction = -1f;
                host.horizontalVelocity = -Mathf.Abs(host.horizontalVelocity) * 0.72f;
                host.impact = 1f;
            }
            else
            {
                host.x = nextX;
                StartFalling(host);
            }
        }
        else host.x = nextX;
    }

    private void StartFalling(Host host)
    {
        host.falling = true;
        host.horizontalVelocity = host.direction * GetBaseSpeed(host.type);
        host.verticalVelocity = host.type == HostType.Robot ? 1.2f : 0f;
        message = "OPEN EDGE! ESCAPE!";
        messageTimer = 1.5f;
    }

    private float GetBaseSpeed(HostType type)
    {
        if (type == HostType.Robot) return robotSpeed;
        if (type == HostType.Cat) return catSpeed;
        if (type == HostType.Brute) return bruteSpeed;
        if (type == HostType.Doll) return 0f;
        return humanSpeed;
    }

    private Floor FindLandingFloor(Host host, float previousWorldY, float currentWorldY, bool includeCurrentFloor)
    {
        if (host.verticalVelocity > 0f || currentWorldY > previousWorldY) return null;

        Floor best = null;
        float bestSurfaceY = float.NegativeInfinity;
        for (int i = 0; i < floors.Count; i++)
        {
            Floor floor = floors[i];
            if (!includeCurrentFloor && floor == host.floor) continue;

            float surfaceY = floor.y + HostGroundOffset;
            bool crossedSurface = previousWorldY >= surfaceY && currentWorldY <= surfaceY;
            bool aboveBest = surfaceY > bestSurfaceY;
            bool overPlatform = host.x >= floor.left + 0.32f && host.x <= floor.right - 0.32f;
            if (crossedSurface && aboveBest && overPlatform)
            {
                best = floor;
                bestSurfaceY = surfaceY;
            }
        }
        return best;
    }

    private void LandHost(Host host, Floor landingFloor)
    {
        bool changedFloor = host.floor != landingFloor;
        host.floor = landingFloor;
        host.verticalOffset = 0f;
        host.falling = false;
        host.impact = 1f;

        if (host.type == HostType.Robot)
            host.verticalVelocity = robotJumpPower;
        else
            host.verticalVelocity = 0f;

        if (host.x < landingFloor.left + 0.42f)
        {
            host.x = landingFloor.left + 0.42f;
            host.direction = 1f;
        }
        else if (host.x > landingFloor.right - 0.42f)
        {
            host.x = landingFloor.right - 0.42f;
            host.direction = -1f;
        }

        if (changedFloor && host.possessed)
        {
            highestY = Mathf.Max(highestY, landingFloor.y);
            cameraKick = 0.16f;
            message = landingFloor.y > 0f ? "LANDED ON ANOTHER FLOOR!" : "SAFE LANDING!";
            messageTimer = 0.65f;
        }
    }

    private void UpdateHostTransform(Host host)
    {
        Vector2 position = GetHostPosition(host);
        host.view.transform.position = position;
        float time = Time.unscaledTime;
        float lean;
        float scaleX = 1f;
        float scaleY = 1f;

        if (host.type == HostType.Human)
        {
            float run = Mathf.Sin(time * 14f + host.phase);
            scaleX = 1f - run * 0.035f + host.impact * 0.12f;
            scaleY = 1f + run * 0.055f - host.impact * 0.14f;
            lean = host.direction * (-7f - Mathf.Abs(host.horizontalVelocity) * 1.2f) + run * 2f;
        }
        else if (host.type == HostType.Robot)
        {
            float airborne = Mathf.Clamp01(host.verticalOffset / 0.7f);
            scaleX = 1f - airborne * 0.08f + host.impact * 0.16f;
            scaleY = 1f + airborne * 0.13f - host.impact * 0.18f;
            lean = host.direction * -9f + host.verticalVelocity * -1.3f;
        }
        else if (host.type == HostType.Doll)
        {
            float idle = Mathf.Sin(time * 2.8f + host.phase);
            scaleX = 1f + idle * 0.025f;
            scaleY = 1f - idle * 0.018f;
            lean = idle * 5f;
            host.verticalOffset = Mathf.Sin(time * 3.2f + host.phase) * 0.035f;
        }
        else if (host.type == HostType.Cat)
        {
            float sprint = Mathf.Abs(host.horizontalVelocity) / Mathf.Max(0.1f, catBurstSpeed);
            float paws = Mathf.Sin(time * (12f + sprint * 16f) + host.phase);
            scaleX = 1f + sprint * 0.18f - paws * 0.025f;
            scaleY = 1f - sprint * 0.12f + paws * 0.04f;
            lean = host.direction * (-4f - sprint * 9f);
        }
        else
        {
            float stomp = Mathf.Sin(time * 6f + host.phase);
            scaleX = 1f + host.impact * 0.1f;
            scaleY = 1f - host.impact * 0.09f + stomp * 0.018f;
            lean = host.direction * -2.5f + stomp * 1.5f;
        }

        // Smaller visuals, unchanged movement distances and collision ranges.
        host.view.transform.localScale = new Vector3(scaleX * 0.72f, scaleY * 0.72f, 1f);
        host.view.transform.localRotation = Quaternion.Euler(0f, host.direction < 0f ? 180f : 0f, lean);
    }

    private Vector2 GetHostPosition(Host host)
    {
        return new Vector2(host.x, host.floor.y + HostGroundOffset + host.verticalOffset);
    }

    private void UpdateCamera(float dt)
    {
        // Follow recoveries downward too, so returning to any previous host remains playable.
        float target = Mathf.Max(2.4f, highestY + 2.1f);
        float targetX = cameraX;
        if (currentHost != null) target = Mathf.Max(2.4f, currentHost.floor.y + 2.1f);
        if (currentHost != null) targetX = currentHost.x;
        if (ghostFlying)
        {
            target = Mathf.Max(2.4f, ghostPosition.y + 0.8f);
            targetX = ghostPosition.x;
        }
        cameraY = Mathf.Lerp(cameraY, target, 1f - Mathf.Exp(-5.5f * dt));
        cameraX = Mathf.Lerp(cameraX, targetX, 1f - Mathf.Exp(-4.2f * dt));
        cameraKick = Mathf.MoveTowards(cameraKick, 0f, dt * 1.8f);
        float shakeX = Mathf.Sin(Time.unscaledTime * 73f) * cameraKick * 0.22f;
        float shakeY = Mathf.Sin(Time.unscaledTime * 91f) * cameraKick * 0.13f;
        gameCamera.transform.position = new Vector3(cameraX + shakeX, cameraY + shakeY, -10f);
    }

    private void Die(string reason)
    {
        if (dead || won) return;
        if (ghostFlying && hasShield && lastPossessedHost != null && reason == "THE GHOST FADED AWAY")
        {
            hasShield = false;
            Host reviveHost = lastPossessedHost;
            reviveHost.falling = false;
            reviveHost.verticalOffset = 0f;
            reviveHost.verticalVelocity = 0f;
            reviveHost.x = Mathf.Clamp(reviveHost.x, reviveHost.floor.left + 0.45f, reviveHost.floor.right - 0.45f);
            Possess(reviveHost);
            message = "SHIELD REVIVE!";
            messageTimer = 1.2f;
            return;
        }
        dead = true;
        dragging = false;
        message = reason;
        messageTimer = 99f;
    }

    private static bool WasRestartPressed()
    {
        bool keyboard = Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame;
        bool pointer = Pointer.current != null && Pointer.current.press.wasPressedThisFrame;
        return keyboard || pointer;
    }

    private GameObject CreateBox(string name, Vector2 position, Vector2 size, Color color, int order, Transform parent)
    {
        GameObject box = new GameObject(name);
        box.transform.SetParent(parent);
        box.transform.position = new Vector3(position.x, position.y, 0f);
        box.transform.localScale = new Vector3(size.x, size.y, 1f);
        SpriteRenderer renderer = box.AddComponent<SpriteRenderer>();
        renderer.sprite = pixelSprite;
        renderer.color = color;
        renderer.sortingOrder = order;
        return box;
    }

    private static Color HostColor(HostType type)
    {
        if (type == HostType.Human) return new Color(0.25f, 0.55f, 1f);
        if (type == HostType.Doll) return new Color(0.95f, 0.34f, 0.58f);
        if (type == HostType.Robot) return new Color(1f, 0.62f, 0.16f);
        if (type == HostType.Cat) return new Color(0.96f, 0.68f, 0.2f);
        return new Color(0.28f, 0.5f, 0.28f);
    }

    private void BuildStyles()
    {
        int baseSize = Mathf.Clamp(Mathf.RoundToInt(Screen.height * 0.027f), 18, 36);
        if (titleStyle != null && titleStyle.fontSize == baseSize) return;
        titleStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = baseSize,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white }
        };
        hudStyle = new GUIStyle(titleStyle) { alignment = TextAnchor.MiddleLeft, fontSize = Mathf.Max(15, baseSize - 4) };
        centerStyle = new GUIStyle(titleStyle) { fontSize = Mathf.RoundToInt(baseSize * 1.55f) };
    }

    private void OnGUI()
    {
        BuildStyles();
        float margin = Mathf.Max(14f, Screen.width * 0.025f);
        float panelHeight = Mathf.Clamp(Screen.height * 0.075f, 58f, 90f);
        DrawRect(new Rect(margin, margin, Screen.width - margin * 2f, panelHeight), new Color(0.02f, 0.03f, 0.07f, 0.8f));
        GUI.Label(new Rect(margin + 18f, margin, Screen.width * 0.5f, panelHeight),
            "HEIGHT  " + Mathf.RoundToInt(highestY) + " M", hudStyle);
        GUI.Label(new Rect(Screen.width * 0.5f, margin, Screen.width * 0.5f - margin - 18f, panelHeight),
            "POSSESSIONS  " + Mathf.Max(0, possessions - 1), titleStyle);

        Color previousBackground = GUI.backgroundColor;
        GUI.backgroundColor = launchMode == LaunchMode.Arc ? new Color(0.25f, 1f, 0.72f) : new Color(0.3f, 0.34f, 0.42f);
        if (GUI.Button(GetArcButtonRect(), "ARC / 포물선"))
        {
            launchMode = LaunchMode.Arc;
            dragging = false;
        }
        GUI.backgroundColor = launchMode == LaunchMode.Straight ? new Color(1f, 0.72f, 0.25f) : new Color(0.3f, 0.34f, 0.42f);
        if (GUI.Button(GetStraightButtonRect(), "STRAIGHT / 직선"))
        {
            launchMode = LaunchMode.Straight;
            dragging = false;
        }
        GUI.backgroundColor = previousBackground;

        Rect speedSlider = GetSpeedSliderRect();
        Rect gravitySlider = GetGravitySliderRect();
        GUI.Label(new Rect(speedSlider.x, speedSlider.y - 23f, speedSlider.width, 24f),
            "LAUNCH SPEED  " + launchSpeedMultiplier.ToString("0.00") + "x", hudStyle);
        launchSpeedMultiplier = GUI.HorizontalSlider(speedSlider, launchSpeedMultiplier, 0.65f, 1.7f);
        GUI.Label(new Rect(gravitySlider.x, gravitySlider.y - 23f, gravitySlider.width, 24f),
            "ARC GRAVITY  " + ghostGravity.ToString("0.0"), hudStyle);
        ghostGravity = GUI.HorizontalSlider(gravitySlider, ghostGravity, 3f, 14f);

        if (messageTimer > 0f)
        {
            Rect notice = new Rect(Screen.width * 0.12f, Screen.height * 0.3f, Screen.width * 0.76f, panelHeight);
            DrawRect(notice, new Color(0.04f, 0.08f, 0.14f, 0.82f));
            GUI.Label(notice, message, titleStyle);
        }

        if (dragging && (currentHost != null || ghostFlying))
        {
            Vector2 start = dragStartScreen;
            Vector2 end = dragCurrentScreen;
            // OnGUI uses a top-left origin.
            start.y = Screen.height - start.y;
            end.y = Screen.height - end.y;
            DrawLine(start, end, new Color(0.28f, 1f, 0.84f, 0.92f), Mathf.Max(5f, Screen.height * 0.007f));
            DrawAimTrajectory();
            GUI.Label(new Rect(end.x - 75f, end.y - 50f, 150f, 42f),
                "POWER " + Mathf.RoundToInt(currentAimPower * 100f) + "%", titleStyle);
        }

        if (dead || won)
        {
            DrawRect(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.68f));
            GUI.Label(new Rect(0f, Screen.height * 0.33f, Screen.width, Screen.height * 0.11f),
                won ? "ROOFTOP!" : "GAME OVER", centerStyle);
            GUI.Label(new Rect(0f, Screen.height * 0.45f, Screen.width, Screen.height * 0.07f), message, titleStyle);
            GUI.Label(new Rect(0f, Screen.height * 0.56f, Screen.width, Screen.height * 0.07f),
                "CLICK / TAP / R  TO RESTART", titleStyle);
        }

        GUI.Label(new Rect(0f, Screen.height - 54f, Screen.width, 42f),
            "DRAG TO AIM   CLOUD " + (hasAirDash ? "READY" : "-") + "   SHIELD " + (hasShield ? "READY" : "-"), titleStyle);
    }

    private void DrawAimTrajectory()
    {
        Vector2 drag = dragCurrentScreen - dragStartScreen;
        if (drag.magnitude < minimumDragPixels || (currentHost == null && !ghostFlying)) return;

        Vector2 direction = drag.normalized;
        float speed = Mathf.Lerp(minimumGhostSpeed, ghostSpeed, currentAimPower) * launchSpeedMultiplier;
        Vector2 startWorld = ghostFlying ? ghostPosition : GetHostPosition(currentHost) + Vector2.up * 0.1f;
        Vector2 previousScreen = WorldToGuiPoint(startWorld);

        for (int i = 1; i <= 13; i++)
        {
            float time = i * 0.09f;
            Vector2 gravityOffset = launchMode == LaunchMode.Arc
                ? Vector2.down * (0.5f * ghostGravity * time * time)
                : Vector2.zero;
            Vector2 world = startWorld + direction * speed * time + gravityOffset;
            Vector2 screen = WorldToGuiPoint(world);
            if (i % 2 == 1)
                DrawLine(previousScreen, screen, new Color(1f, 0.9f, 0.35f, 0.88f), Mathf.Max(4f, Screen.height * 0.005f));
            previousScreen = screen;
        }
    }

    private Vector2 WorldToGuiPoint(Vector2 world)
    {
        Vector2 screen = gameCamera.WorldToScreenPoint(world);
        screen.y = Screen.height - screen.y;
        return screen;
    }

    private static void DrawRect(Rect rect, Color color)
    {
        Color previous = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = previous;
    }

    private static void DrawLine(Vector2 start, Vector2 end, Color color, float width)
    {
        Matrix4x4 previousMatrix = GUI.matrix;
        Color previousColor = GUI.color;
        float angle = Vector3.Angle(end - start, Vector2.right);
        if (start.y > end.y) angle = -angle;
        GUI.color = color;
        GUIUtility.RotateAroundPivot(angle, start);
        GUI.DrawTexture(new Rect(start.x, start.y - width * 0.5f, (end - start).magnitude, width), Texture2D.whiteTexture);
        GUI.matrix = previousMatrix;
        GUI.color = previousColor;
    }
}
