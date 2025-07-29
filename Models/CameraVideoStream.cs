namespace wpfhikip.Models
{
    public class CameraVideoStream : BaseNotifyPropertyChanged
    {
        private string? _mainStreamUrl;
        private string? _subStreamUrl;
        private string? _resolution;
        private string? _frameRate;
        private string? _bitRate;

        public string? MainStreamUrl
        {
            get => _mainStreamUrl;
            set => SetProperty(ref _mainStreamUrl, value);
        }

        public string? SubStreamUrl
        {
            get => _subStreamUrl;
            set => SetProperty(ref _subStreamUrl, value);
        }

        public string? Resolution
        {
            get => _resolution;
            set => SetProperty(ref _resolution, value);
        }

        public string? FrameRate
        {
            get => _frameRate;
            set => SetProperty(ref _frameRate, value);
        }

        public string? BitRate
        {
            get => _bitRate;
            set => SetProperty(ref _bitRate, value);
        }
    }
}