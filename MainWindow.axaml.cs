using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Timer = System.Timers.Timer;
using Newtonsoft.Json;

namespace MMPET;

public partial class MainWindow : Window
{
    private BattleEntitiesAPI? _api;
    private Timer? _refreshTimer;
    private Timer? _positionTimer;
    private Timer? _validationTimer;
    private Dictionary<string, string> _petNames = new();
    private ObservableCollection<PetDisplayInfo> _petList = new();
    private OverlayWindow? _overlayWindow;
    private bool _overlayEnabled = false;
    public float LabelFontSize { get; set; } = 10f;

    


    public MainWindow()
    {
        InitializeComponent();
        LoadPetNames();
        PetListBox.ItemsSource = _petList;
        
        // 设置定时刷新（每500毫秒更新列表信息）
        _refreshTimer = new Timer(500);
        _refreshTimer.Elapsed += RefreshTimer_Elapsed;
        
        // 设置位置更新定时器（每20毫秒更新位置信息）
        _positionTimer = new Timer(20);
        _positionTimer.Elapsed += PositionTimer_Elapsed;
        
        // 设置标签验证定时器（每500毫秒检查标签是否对应有效宠物）
        _validationTimer = new Timer(500);
        _validationTimer.Elapsed += ValidationTimer_Elapsed;
    }

    private void LoadPetNames()
    {
        try
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            var resourceName = "MMPET.PetName.json";
            
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream != null)
            {
                using var reader = new StreamReader(stream);
                string json = reader.ReadToEnd();
                _petNames = JsonConvert.DeserializeObject<Dictionary<string, string>>(json) ?? new();
                StatusText.Text = $"已加载 {_petNames.Count} 个宠物名称映射";
            }
            else
            {
                StatusText.Text = "未找到嵌入的宠物名称资源";
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"加载宠物名称失败: {ex.Message}";
        }
    }

    private async void ConnectButton_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            ConnectButton.IsEnabled = false;
            StatusText.Text = "正在连接进程...";

            await Task.Run(() =>
            {
                _api = new BattleEntitiesAPI("EM-Win64-Shipping");
            });

            OverlayButton.IsEnabled = true;
            StatusText.Text = "已连接到 EM-Win64-Shipping 进程";
            
            // 开始定时刷新
            _refreshTimer?.Start();
            _positionTimer?.Start();
            _validationTimer?.Start();
            
            // 立即刷新一次
            await RefreshPets();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"连接失败: {ex.Message}";
            ConnectButton.IsEnabled = true;
        }
    }







    private void OverlayButton_Click(object? sender, RoutedEventArgs e)
    {
        if (!_overlayEnabled)
        {
            // 开启覆盖层
            _overlayWindow = new OverlayWindow();
            _overlayWindow.SetAPI(_api!);
            _overlayWindow.LabelFontSize = LabelFontSize;
            _overlayWindow.Show(); // 显示覆盖窗口
            
            _overlayEnabled = true;
            OverlayButton.Content = "关闭覆盖";
            OverlayButton.Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Colors.Red);
            StatusText.Text = "覆盖层已开启";
        }
        else
        {
            // 关闭覆盖层
            _overlayWindow?.Close();
            _overlayWindow = null;
            
            _overlayEnabled = false;
            OverlayButton.Content = "开启覆盖";
            OverlayButton.Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(255, 107, 53));
            StatusText.Text = "覆盖层已关闭";
        }
    }

    private void FontSizeSlider_ValueChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        var fontSize = (float)e.NewValue;
        LabelFontSize = fontSize;
        FontSizeText.Text = fontSize.ToString("F0");
        
        if (_overlayWindow != null)
        {
            _overlayWindow.LabelFontSize = fontSize;
        }
    }





    private async void RefreshTimer_Elapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        await RefreshPets();
    }

    private void PositionTimer_Elapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        // 只更新宠物位置信息，不重新获取宠物列表
        if (_overlayEnabled && _overlayWindow != null && _petList.Count > 0 && _api != null)
        {
            // 在后台线程更新宠物位置
            Task.Run(() =>
            {
                try
                {
                    // 更新每个宠物的位置信息
                    foreach (var pet in _petList)
                    {
                        if (pet.EntityInfo != null)
                        {
                            _api.RefreshEntityPosition(pet.EntityInfo);
                        }
                    }

                    // 在UI线程更新覆盖层
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        _overlayWindow.UpdatePetOverlay(_petList.ToList());
                        // 定期强制设置为最顶层
                        _overlayWindow.ForceTopMost();
                    });
                }
                catch (Exception ex)
                {
                    // 位置更新失败时不影响主流程
                    Console.WriteLine($"位置更新失败: {ex.Message}");
                }
            });
        }
    }

    private void ValidationTimer_Elapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        // 检查覆盖层标签对应的宠物是否还在主界面列表中
        if (_overlayEnabled && _overlayWindow != null && _petList.Count > 0 && _api != null)
        {
            Task.Run(() =>
            {
                try
                {
                    // 获取当前的NPC地图
                    var currentNpcs = _api.GetNpcMap();
                    var activeNpcs = currentNpcs.Where(entity => 
                        (entity.ClassName.Contains("BP_PetNPC_Common") ||
                         entity.ParentClasses.Any(c => c.Contains("NpcCharacter"))) &&
                        entity.InteractiveState == ENpcPetState.Active
                    ).ToList();
                    
                    var currentNpcIds = new HashSet<int>(activeNpcs.Select(n => n.EntityId));
                    
                    // 过滤出仍然存在的宠物
                    var validPets = _petList.Where(pet => 
                        pet.EntityInfo != null && 
                        currentNpcIds.Contains(pet.EntityInfo.EntityId)
                    ).ToList();

                    // 在UI线程更新覆盖层，移除不存在的宠物标签
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        _overlayWindow.UpdatePetOverlay(validPets);
                    });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"标签验证失败: {ex.Message}");
                }
            });
        }
    }

    private async Task RefreshPets()
    {
        if (_api == null) return;

        try
        {
            await Task.Run(() =>
            {
                // 获取 NPC 地图中的宠物，只显示可交互状态的宠物
                var npcEntities = _api.GetNpcMap();
                var petEntities = npcEntities.Where(entity => 
                    (entity.ClassName.Contains("BP_PetNPC_Common") ||
                     entity.ParentClasses.Any(c => c.Contains("NpcCharacter"))) &&
                    entity.InteractiveState == ENpcPetState.Active
                ).ToList();

                // 更新UI（需要在UI线程中执行）
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    UpdatePetList(petEntities);
                });
            });
        }
        catch (Exception ex)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                StatusText.Text = $"刷新失败: {ex.Message}";
            });
        }
    }

    private void UpdatePetList(List<EntityInfo> petEntities)
    {
        _petList.Clear();

        foreach (var pet in petEntities)
        {
            try
            {
                // 从 ANpcCharacter->NpcId 读取宠物ID
                int npcId = ReadNpcId(pet.EntityPtr);
                string petName = GetPetName(npcId);
                
                var displayInfo = new PetDisplayInfo
                {
                    DisplayName = petName,
                    Details = $"类名: {pet.ClassName} | 实体ID: {pet.EntityId}",
                    Position = $"位置: {pet.Position}",
                    NpcId = npcId,
                    EntityInfo = pet
                };

                _petList.Add(displayInfo);
            }
            catch (Exception)
            {
                // 如果读取失败，仍然显示基本信息
                var displayInfo = new PetDisplayInfo
                {
                    DisplayName = pet.Name,
                    Details = $"类名: {pet.ClassName} | 实体ID: {pet.EntityId}",
                    Position = $"位置: {pet.Position}",
                    NpcId = 0,
                    EntityInfo = pet
                };

                _petList.Add(displayInfo);
            }
        }

        StatusText.Text = $"找到 {_petList.Count} 个宠物实体";
    }

    private int ReadNpcId(IntPtr npcPtr)
    {
        // ANpcCharacter->NpcId = 0x2140
        const int OFFSET_NPCID = 0x2140;
        
        if (_api == null) return 0;
        
        return _api.ReadInt32(new IntPtr(npcPtr.ToInt64() + OFFSET_NPCID));
    }

    private string GetPetName(int npcId)
    {
        string npcIdStr = npcId.ToString();
        if (_petNames.ContainsKey(npcIdStr))
        {
            return _petNames[npcIdStr];
        }
        return $"未知宠物 ({npcId})";
    }

    protected override void OnClosed(EventArgs e)
    {
        _refreshTimer?.Stop();
        _refreshTimer?.Dispose();
        _positionTimer?.Stop();
        _positionTimer?.Dispose();
        _validationTimer?.Stop();
        _validationTimer?.Dispose();
        _overlayWindow?.Close();
        base.OnClosed(e);
    }
}

public class PetDisplayInfo
{
    public string DisplayName { get; set; } = "";
    public string Details { get; set; } = "";
    public string Position { get; set; } = "";
    public int NpcId { get; set; }
    public EntityInfo? EntityInfo { get; set; }
}