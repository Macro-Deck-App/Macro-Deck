using System.Windows.Forms;

namespace SuchByte.MacroDeck.GUI.CustomControls
{
    public partial class Pagination : UserControl
    {
        private int _currentPage = 1;
        private int _pages = 1;

        public int CurrentPage
        {
            get => _currentPage;
            set
            {
                if (value == _currentPage)
                {
                    return;
                }

                _currentPage = value;
                UpdatePage();
            }
        }

        public int Pages
        {
            get => _pages;
            set
            {
                if (value == _pages)
                {
                    return;
                }

                _pages = value;
                UpdatePage();
            }
        }

        public event Action<int>? PageUpdated;

        public Pagination()
        {
            InitializeComponent();
        }

        private void UpdatePage()
        {
            lblPage.Text = $"{_currentPage} / {_pages}";
            btnFirstPage.Enabled = _currentPage > 1;
            btnPreviousPage.Enabled = _currentPage > 1;
            btnLastPage.Enabled = _currentPage < _pages;
            btnNextPage.Enabled = _currentPage < _pages;
        }

        private void BtnFirstPage_Click(object sender, EventArgs e)
        {
            if (_currentPage <= 1)
            {
                return;
            }

            CurrentPage = 1;
            PageUpdated?.Invoke(_currentPage);
        }

        private void BtnPreviousPage_Click(object sender, EventArgs e)
        {
            if (_currentPage <= 1)
            {
                return;
            }

            CurrentPage--;
            PageUpdated?.Invoke(_currentPage);
        }

        private void BtnNextPage_Click(object sender, EventArgs e)
        {
            if (_currentPage >= _pages)
            {
                return;
            }

            CurrentPage++;
            PageUpdated?.Invoke(_currentPage);
        }

        private void BtnLastPage_Click(object sender, EventArgs e)
        {
            if (_currentPage >= _pages)
            {
                return;
            }

            CurrentPage = _pages;
            PageUpdated?.Invoke(_currentPage);
        }
    }
}
