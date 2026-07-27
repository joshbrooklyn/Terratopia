using Godot;
using GameEngine.Engine;

public partial class GameEngineAutoload : Node
{
	public static GameEngineClass Engine => GameEngineClass.Instance;

	public override void _Ready()
	{
		System.Threading.Tasks.Task.Run(() =>
		{
			_ = GameEngineClass.Instance; // content loading happens here, off the main thread
			System.Threading.Thread.Sleep(3000); // simulate loading time for testing
			GameEngineClass.Instance.CompleteStartup();
		});
	}
}
