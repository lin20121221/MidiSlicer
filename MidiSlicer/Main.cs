﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using M;
namespace MidiSlicer
{
	public partial class Main : Form
	{
		MidiFile _file;
		Thread _previewThread;
		string _tracksLabelFormat;
		public Main()
		{
			InitializeComponent();
			_tracksLabelFormat = TracksLabel.Text;
			UnitsCombo.SelectedIndex = 0;
			StartCombo.SelectedIndex = 0;
			_UpdateMidiFile();

		}

		private void BrowseButton_Click(object sender, EventArgs e)
		{
			var res = OpenMidiFile.ShowDialog(this);
			if (DialogResult.OK == res)
			{
				MidiFileBox.Text = OpenMidiFile.FileName;
				_UpdateMidiFile();
			}
		}
		void _UpdateMidiFile()
		{
			var exists = false;
			try
			{
				if (File.Exists(MidiFileBox.Text))
					exists = true;
			}
			catch { }
			TrackList.Items.Clear();
			if (!exists)
			{
				TracksLabel.Text = "";
				MidiFileBox.ForeColor = Color.Red;
				_file = null;
				TrackList.Enabled = false;
				PreviewButton.Enabled = false;
				UnitsCombo.Enabled = false;
				StartCombo.Enabled = false;
				OffsetUpDown.Enabled = false;
				LengthUpDown.Enabled = false;
				StretchUpDown.Enabled = false;
				MergeTracksCheckBox.Enabled = false;
				CopyTimingPatchCheckBox.Enabled = false;
				AdjustTempoCheckBox.Enabled = false;
				ResampleUpDown.Enabled = false;
				NormalizeCheckBox.Enabled = false;
				LevelsUpDown.Enabled = false;
				SaveAsButton.Enabled = false;
			}
			else
			{
				MidiFileBox.ForeColor = SystemColors.WindowText;
				using (Stream stm = File.OpenRead(MidiFileBox.Text))
					 _file = MidiFile.ReadFrom(stm);
				var i = 0;
				foreach (var trk in _file.Tracks)
				{
					var s = trk.Name;
					if (string.IsNullOrEmpty(s))
						s = "Track #" + i.ToString();
					TrackList.Items.Add(s, true);
					++i;
				}
				var sig = _file.TimeSignature;
				TracksLabel.Text = string.Format(_tracksLabelFormat, _file.Tempo,sig.Numerator,sig.Denominator);
				TrackList.Enabled = true;
				PreviewButton.Enabled = true;
				UnitsCombo.Enabled = true;
				StartCombo.Enabled = true;
				OffsetUpDown.Enabled = true;
				LengthUpDown.Enabled = true;
				StretchUpDown.Enabled = true;
				MergeTracksCheckBox.Enabled = true;
				CopyTimingPatchCheckBox.Enabled = true;
				AdjustTempoCheckBox.Enabled = true;
				ResampleUpDown.Enabled = true;
				NormalizeCheckBox.Enabled = true;
				LevelsUpDown.Enabled = true;
				SaveAsButton.Enabled = true;
				StretchUpDown.Value = 1;
				UnitsCombo.SelectedIndex = 0;
				StartCombo.SelectedIndex = 0;
				ResampleUpDown.Value = _file.TimeBase;
				if (0 == UnitsCombo.SelectedIndex) // beats
				{
					LengthUpDown.Maximum = _file.Length / (decimal)_file.TimeBase;
					OffsetUpDown.Maximum = LengthUpDown.Maximum - 1;
				}
				else // ticks
				{
					LengthUpDown.Maximum = _file.Length;
					OffsetUpDown.Maximum = LengthUpDown.Maximum - 1;
				}
				LengthUpDown.Value = LengthUpDown.Maximum;
			}
		}

		private void MidiFileBox_Leave(object sender, EventArgs e)
		{
			_UpdateMidiFile();
		}

		private void PreviewButton_Click(object sender, EventArgs e)
		{
			if("Stop"==PreviewButton.Text)
			{
				if (null != _previewThread)
				{
					_previewThread.Abort();
					_previewThread.Join();
					_previewThread = null;
				}
				PreviewButton.Text = "Preview";
				return;
			}
			
			if (null != _previewThread)
			{
				_previewThread.Abort();
				_previewThread.Join();
				_previewThread = null;
			}
			PreviewButton.Text = "Stop";
			var f = _ProcessFile();
			_previewThread = new Thread(() => { f.Preview(0,true); });
			_previewThread.Start();
		}
		protected override void OnClosing(CancelEventArgs e)
		{
			if (null != _previewThread)
			{
				_previewThread.Abort();
				_previewThread.Join();
				_previewThread = null;
			}

		}

		private void UnitsCombo_SelectedIndexChanged(object sender, EventArgs e)
		{
			OffsetUpDown.Value = 0;

			if(null!=_file) // sanity
			{
				if(0==UnitsCombo.SelectedIndex) // beats
				{
					LengthUpDown.Maximum = Math.Ceiling(_file.Length / (decimal)_file.TimeBase);
					OffsetUpDown.Maximum = LengthUpDown.Maximum - 1;
				} else // ticks
				{
					LengthUpDown.Maximum = _file.Length;
					OffsetUpDown.Maximum = LengthUpDown.Maximum - 1;
				}
				LengthUpDown.Value = LengthUpDown.Maximum;
			}
		}

		private void SaveAsButton_Click(object sender, EventArgs e)
		{
			var res = SaveMidiFile.ShowDialog(this);
			if (DialogResult.OK == res)
			{
				var mf = _ProcessFile();
				using (var stm = File.OpenWrite(SaveMidiFile.FileName))
				{
					stm.SetLength(0);
					mf.WriteTo(stm);
				}
			}
		}
		MidiFile _ProcessFile()
		{
			var result = _file.Clone();
			if (ResampleUpDown.Value != _file.TimeBase)
				result = result.Resample(unchecked((short)ResampleUpDown.Value));
			if (NormalizeCheckBox.Checked)
				result = result.NormalizeVelocities();
			if(1m!=LevelsUpDown.Value)
				result = result.ScaleVelocities((double)LevelsUpDown.Value);
			var ofs = OffsetUpDown.Value;
			var len = LengthUpDown.Value;
			if (0 == UnitsCombo.SelectedIndex) // beats
			{
				len = Math.Min(len * _file.TimeBase, _file.Length);
				ofs = Math.Min(ofs * _file.TimeBase, _file.Length);
			}
			switch (StartCombo.SelectedIndex)
			{
				case 1:
					ofs += result.FirstDownBeat;
					break;
				case 2:
					ofs += result.FirstNoteOn;
					break;
			}
			
			var nseq = new MidiSequence();
			if(0!=ofs && CopyTimingPatchCheckBox.Checked)
			{
				var mtrk = MidiSequence.Merge(result.Tracks);
				var end = mtrk.FirstNoteOn;
				if (0 == end)
					end = mtrk.Length;
				var ins = 0;
				for (int ic = mtrk.Events.Count, i = 0; i < ic; ++i)
				{
					var ev = mtrk.Events[i];
					if (ev.Position >= end)
						break;
					var m = ev.Message;
					switch (m.Status)
					{
						case 0xFF:
							var mm = m as MidiMessageMeta;
							switch (mm.Data1)
							{
								case 0x51:
								case 0x54:
									if (0 == nseq.Events.Count)
										nseq.Events.Add(new MidiEvent(0,ev.Message.Clone()));
									else
										nseq.Events.Insert(ins, new MidiEvent(0,ev.Message.Clone()));
									++ins;
									break;
							}
							break;
						default:
							if (0xC0 == (ev.Message.Status & 0xF0))
							{
								if (0 == nseq.Events.Count)
									nseq.Events.Add(new MidiEvent(0, ev.Message.Clone()));
								else
									nseq.Events.Insert(ins, new MidiEvent(0, ev.Message.Clone()));
								++ins;
							}
							break;
					}
				}
			}
			var hasTrack0 = TrackList.GetItemChecked(0);
			if (0!=ofs || result.Length!=len)
				result = result.GetRange((int)ofs, (int)len,false);
			
			var l = new List<MidiSequence>(result.Tracks);
			result.Tracks.Clear();
			for(int ic=l.Count,i=0;i<ic;++i)
			{
				if(TrackList.GetItemChecked(i))
				{
					result.Tracks.Add(l[i]);
				}
			}
			if (0 < nseq.Events.Count)
			{
				if(!hasTrack0)
					result.Tracks.Insert(0,nseq);
				else
				{
					for(var i = nseq.Events.Count-1;0<=i;--i)
					{
						result.Tracks[0].Events.Insert(0, nseq.Events[i]);
					}
				}
			}
			var endTrack = new MidiSequence();
			// add end marker to new track
			// HACK: For some reason, adding the MIDI end track message (Meta type 0x2F)
			// causes a pause in playback (preceeding or following i'm not sure)
			// I haven't been able to track it down so i'm sending a note off
			// message instead on note C octave 0" - it's just a placeholder
			// so the track doesn't end early.
			var msg = new MidiMessageNoteOff(0,0,0);
			//var msg = new MidiMessageMeta(0x2f,new byte[0]);
			endTrack.Events.Add(new MidiEvent((int)len, msg));
			// merge new track with track zero
			result.Tracks[0] = MidiSequence.Merge(result.Tracks[0], endTrack);
			if (1m != StretchUpDown.Value)
				result = result.Stretch((double)StretchUpDown.Value, AdjustTempoCheckBox.Checked);

			if (MergeTracksCheckBox.Checked)
			{
				var trk = MidiSequence.Merge(result.Tracks);
				result.Tracks.Clear();
				result.Tracks.Add(trk);
			}
			return result;
		}
	}
}
