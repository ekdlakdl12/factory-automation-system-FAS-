using factory_automation_system_FAS_.Utils;
using Microsoft.Win32;
using System;
using System.ComponentModel;
using System.Data;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

namespace factory_automation_system_FAS_.ViewModels
{
    /// <summary>
    /// DB 연동 전 단계(MVP):
    /// - "스키마(컬럼)부터" UI에 박아두고
    /// - 실제 데이터는 나중에 DB/서버 연결되면 채우는 방식
    /// </summary>
    public sealed class ProductionQueryViewModel : INotifyPropertyChanged
    {
        public DataTable WorkOrderTable { get; }
        public DataTable VisionEventTable { get; }
        public DataTable TraceLogTable { get; }
        public DataTable InventoryTable { get; }

        private int _selectedTabIndex;
        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set
            {
                if (_selectedTabIndex == value) return;
                _selectedTabIndex = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ActiveTable));
                OnPropertyChanged(nameof(ActiveTableView));
                OnPropertyChanged(nameof(ActiveHasRows));
            }
        }

        public DataTable ActiveTable => SelectedTabIndex switch
        {
            0 => WorkOrderTable,
            1 => VisionEventTable,
            2 => TraceLogTable,
            3 => InventoryTable,
            _ => WorkOrderTable
        };

        public DataView ActiveTableView => ActiveTable.DefaultView;
        public bool ActiveHasRows => ActiveTable.Rows.Count > 0;

        public ICommand ExportCsvCommand { get; }

        public ProductionQueryViewModel()
        {
            WorkOrderTable = CreateWorkOrderSchema();
            VisionEventTable = CreateVisionEventSchema();
            TraceLogTable = CreateTraceLogSchema();
            InventoryTable = CreateInventorySchema();

            ExportCsvCommand = new RelayCommand(ExportCsv);
            SelectedTabIndex = 0;
        }

        // =====================
        // Schema (DB 테이블과 1:1 매핑)
        // =====================

        private static DataTable CreateWorkOrderSchema()
        {
            // Product_WorkOrder
            var dt = new DataTable("Product_WorkOrder");
            dt.Columns.Add("product_id", typeof(int));
            dt.Columns.Add("wo_id", typeof(string));
            dt.Columns.Add("raw_id", typeof(int));
            dt.Columns.Add("start_time", typeof(string));
            dt.Columns.Add("end_time", typeof(string));
            dt.Columns.Add("status", typeof(string));
            return dt;
        }

        private static DataTable CreateVisionEventSchema()
        {
            // VisionEvent
            var dt = new DataTable("VisionEvent");
            dt.Columns.Add("event_id", typeof(int));
            dt.Columns.Add("conv_id", typeof(int));
            dt.Columns.Add("image_ref", typeof(string));
            dt.Columns.Add("detected_class", typeof(string));
            dt.Columns.Add("confidence", typeof(double));
            dt.Columns.Add("ts", typeof(string));
            dt.Columns.Add("meta", typeof(string)); // JSON string
            return dt;
        }

        private static DataTable CreateTraceLogSchema()
        {
            // TraceLog
            var dt = new DataTable("TraceLog");
            dt.Columns.Add("trace_id", typeof(int));
            dt.Columns.Add("entity_type", typeof(string));
            dt.Columns.Add("entity_id", typeof(int));
            dt.Columns.Add("action", typeof(string));
            dt.Columns.Add("ts", typeof(string));
            dt.Columns.Add("user_id", typeof(string));
            dt.Columns.Add("detail", typeof(string)); // JSON string
            return dt;
        }

        private static DataTable CreateInventorySchema()
        {
            // Inventory
            var dt = new DataTable("Inventory");
            dt.Columns.Add("inv_id", typeof(int));
            dt.Columns.Add("material_id", typeof(int));
            dt.Columns.Add("qty", typeof(int));
            dt.Columns.Add("location", typeof(string));
            dt.Columns.Add("updated_at", typeof(string));
            return dt;
        }

        // =====================
        // Export
        // =====================

        private void ExportCsv()
        {
            try
            {
                var name = ActiveTable.TableName;
                var dlg = new SaveFileDialog
                {
                    Title = $"{name} CSV 저장",
                    Filter = "CSV (*.csv)|*.csv",
                    FileName = $"{name}_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
                };

                if (dlg.ShowDialog() != true)
                    return;

                CsvUtil.WriteDataTableToCsv(ActiveTable, dlg.FileName);
                MessageBox.Show("CSV 저장 완료!", "OK", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"CSV 저장 실패: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
