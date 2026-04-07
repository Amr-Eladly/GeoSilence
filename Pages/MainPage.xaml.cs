using GeoSilence.Models;
using GeoSilence.PageModels;

namespace GeoSilence.Pages
{
    public partial class MainPage : ContentPage
    {
        private readonly HomeViewModel _vm;

        public MainPage(HomeViewModel vm)
        {
            InitializeComponent();
            BindingContext = vm;
            _vm = vm;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            await _vm.LoadAsync();
        }
    }
}