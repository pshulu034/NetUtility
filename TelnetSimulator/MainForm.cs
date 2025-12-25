namespace TelnetSimulator
{
    public partial class MainForm : Form
    {
        TelnetServer server = new TelnetServer();
        public MainForm()
        {
            InitializeComponent();
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            server.Start();
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            server.Stop();
        }
    }
}
