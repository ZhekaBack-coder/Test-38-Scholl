using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.Animations;

public class SpriteSheetImporter : EditorWindow
{
    private Texture2D sourceTexture;
    private AnimatorController existingController; // Поле для твого існуючого контролера
    private float frameRate = 12f;

    private const int TOTAL_ROWS = 8;
    private const int COLS_PER_ROW = 24;

    struct AnimDefinition
    {
        public string Name;
        public int StartCol;
        public int EndCol;
        public bool Loop;

        public AnimDefinition(string name, int start, int end, bool loop)
        {
            Name = name;
            StartCol = start;
            EndCol = end;
            Loop = loop;
        }
    }

    private List<AnimDefinition> GetDefinitions()
    {
        return new List<AnimDefinition>()
        {
            new AnimDefinition("Idle", 1, 2, true),
            new AnimDefinition("Walk", 2, 5, true),
            new AnimDefinition("Sword", 6, 9, false),
            new AnimDefinition("Bow", 10, 13, false),
            new AnimDefinition("Wand", 14, 16, false),
            new AnimDefinition("Throw", 17, 19, false),
            new AnimDefinition("Hurt", 20, 22, false),
            new AnimDefinition("Death", 23, 24, false)
        };
    }

    private readonly string[] rowNames = { "Down", "DownRight", "Right", "UpRight", "Up", "UpLeft", "Left", "DownLeft" };
    private readonly Vector2[] blendDirections = {
        new Vector2(0, -1),   // Down
        new Vector2(0.7f, -0.7f),   // DownRight
        new Vector2(1, 0),    // Right
        new Vector2(0.7f, 0.7f),    // UpRight
        new Vector2(0, 1),    // Up
        new Vector2(-0.7f, 0.7f),   // UpLeft
        new Vector2(-1, 0),   // Left
        new Vector2(-0.7f, -0.7f)   // DownLeft
    };

    [MenuItem("Tools/RPG Animator Generator")]
    public static void ShowWindow() => GetWindow<SpriteSheetImporter>("RPG Animator Gen").Show();

    void OnGUI()
    {
        GUILayout.Label("Оновлення Blend Trees (Safe Mode)", EditorStyles.boldLabel);

        sourceTexture = (Texture2D)EditorGUILayout.ObjectField("Spritesheet", sourceTexture, typeof(Texture2D), false);
        existingController = (AnimatorController)EditorGUILayout.ObjectField("Твій Animator Controller", existingController, typeof(AnimatorController), false);
        frameRate = EditorGUILayout.FloatField("Frame Rate", frameRate);

        if (GUILayout.Button("Оновити тільки Blend Trees", GUILayout.Height(40)))
        {
            if (sourceTexture == null || existingController == null)
            {
                EditorUtility.DisplayDialog("Помилка", "Обери і текстуру, і свій контролер!", "Ок");
                return;
            }
            UpdateController();
        }
    }

    void UpdateController()
    {
        string path = AssetDatabase.GetAssetPath(sourceTexture);
        string rootFolder = Path.GetDirectoryName(path);
        string animFolder = Path.Combine(rootFolder, sourceTexture.name + "_Generated");

        if (!AssetDatabase.IsValidFolder(animFolder)) AssetDatabase.CreateFolder(rootFolder, sourceTexture.name + "_Generated");

        Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
        List<Sprite> sprites = assets.OfType<Sprite>().OrderBy(s => {
            string[] split = s.name.Split('_');
            return int.TryParse(split.Last(), out int id) ? id : 999;
        }).ToList();

        var definitions = GetDefinitions();
        var states = existingController.layers[0].stateMachine.states;

        foreach (var def in definitions)
        {
            // 1. Шукаємо існуючий стейт за назвою
            ChildAnimatorState foundState = states.FirstOrDefault(s => s.state.name == def.Name);
            if (foundState.state == null)
            {
                Debug.LogWarning($"Стейт {def.Name} не знайдено в контролері. Пропускаю.");
                continue;
            }

            // 2. Створюємо Blend Tree правильно
            BlendTree blendTree = new BlendTree();
            blendTree.name = def.Name + "_BT";
            blendTree.blendType = BlendTreeType.SimpleDirectional2D;
            blendTree.blendParameter = "InputX";
            blendTree.blendParameterY = "InputY";

            // ВАЖЛИВО: Додаємо BlendTree як частину файлу контролера, щоб він не зник!
            AssetDatabase.AddObjectToAsset(blendTree, existingController);

            for (int row = 0; row < TOTAL_ROWS; row++)
            {
                AnimationClip clip = new AnimationClip { frameRate = frameRate };
                if (def.Loop)
                {
                    var settings = AnimationUtility.GetAnimationClipSettings(clip);
                    settings.loopTime = true;
                    AnimationUtility.SetAnimationClipSettings(clip, settings);
                }

                List<ObjectReferenceKeyframe> keyFrames = new List<ObjectReferenceKeyframe>();
                for (int col = def.StartCol; col <= def.EndCol; col++)
                {
                    int spriteIndex = (row * COLS_PER_ROW) + (col - 1);
                    keyFrames.Add(new ObjectReferenceKeyframe
                    {
                        time = (col - def.StartCol) / frameRate,
                        value = sprites[spriteIndex]
                    });
                }

                AnimationUtility.SetObjectReferenceCurve(clip, new EditorCurveBinding
                {
                    type = typeof(SpriteRenderer),
                    path = "",
                    propertyName = "m_Sprite"
                }, keyFrames.ToArray());

                string clipName = $"{def.Name}_{rowNames[row]}.anim";
                AssetDatabase.CreateAsset(clip, Path.Combine(animFolder, clipName));
                blendTree.AddChild(clip, blendDirections[row]);
            }

            // 3. Призначаємо нове дерево у твій стейт
            foundState.state.motion = blendTree;
        }

        EditorUtility.SetDirty(existingController); // Помічаємо контролер як змінений
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("Успіх", "Blend Trees відновлено та жорстко збережено у файлі!", "Дякую!");
    }
}