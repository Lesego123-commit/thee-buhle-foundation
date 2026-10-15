// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Google (2026) Android Developers documentation.
//    - Google (2026) Material Design 3.
// =============================================================================

package za.co.theebuhlefoundation.ui.progress

import android.os.Bundle
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import androidx.core.content.ContextCompat
import androidx.core.os.bundleOf
import androidx.fragment.app.Fragment
import androidx.fragment.app.viewModels
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.lifecycleScope
import androidx.lifecycle.repeatOnLifecycle
import androidx.navigation.fragment.findNavController
import kotlinx.coroutines.launch
import za.co.theebuhlefoundation.R
import za.co.theebuhlefoundation.data.api.dto.LearnerProgress
import za.co.theebuhlefoundation.databinding.FragmentProgressBinding
import za.co.theebuhlefoundation.ui.common.*

class ProgressFragment : Fragment() {

    private var _binding: FragmentProgressBinding? = null
    private val binding get() = _binding!!

    private lateinit var state: StateViews

    private val viewModel: ProgressViewModel by viewModels {
        ContainerViewModelFactory(container) {
            ProgressViewModel(it.auth, it.learning, it.progress)
        }
    }

    override fun onCreateView(
        inflater: LayoutInflater,
        parent: ViewGroup?,
        savedInstanceState: Bundle?,
    ): View {
        _binding = FragmentProgressBinding.inflate(inflater, parent, false)

        return binding.root
    }

    override fun onViewCreated(view: View, savedInstanceState: Bundle?) {
        state = StateViews(binding.stateHost, binding.refresh)
        state.onRetry { viewModel.load() }

        binding.refresh.setOnRefreshListener { viewModel.load(refreshing = true) }

        viewLifecycleOwner.lifecycleScope.launch {
            viewLifecycleOwner.repeatOnLifecycle(Lifecycle.State.STARTED) {
                viewModel.state.collect { current ->
                    binding.refresh.isRefreshing = false

                    state.render(current, ::render)
                }
            }
        }

        viewModel.load()
    }

    private fun render(progress: LearnerProgress) {
        binding.learnerName.text = "${progress.firstName} ${progress.surname}"
        binding.learnerMeta.text = getString(
            R.string.progress_meta,
            progress.referenceCode,
            progress.grade,
        )
        binding.streak.text = resources.getQuantityString(
            R.plurals.streak_sessions,
            progress.streak,
            progress.streak,
        )

        binding.panel.removeAllViews()

        // Marks, with the sign shown. A subject that has gone backwards is the
        // one a parent most needs to see, so a negative is coloured rather than
        // rounded away.
        binding.panel.addSection(
            getString(R.string.progress_marks),
            progress.marks.map { mark ->
                RowContent(
                    title = mark.subjectName,
                    subtitle = getString(
                        R.string.progress_mark_detail,
                        mark.firstMark,
                        mark.latestMark,
                        mark.entries,
                    ),
                    pill = Format.improvement(mark.improvement),
                    pillColour = ContextCompat.getColor(
                        requireContext(),
                        if (mark.improvement < 0) R.color.clay else R.color.forest,
                    ),
                )
            },
        )

        binding.panel.addSection(
            getString(R.string.home_attendance),
            listOf(
                RowContent(
                    title = getString(R.string.progress_attended),
                    subtitle = getString(
                        R.string.home_attendance_note,
                        progress.attendance.sessionsAttended,
                        progress.attendance.sessionsRecorded,
                    ),
                    pill = Format.percent(progress.attendance.attendanceRate),
                ),
                RowContent(
                    title = getString(R.string.progress_late),
                    pill = progress.attendance.sessionsLate.toString(),
                ),
                RowContent(
                    title = getString(R.string.progress_absent),
                    pill = progress.attendance.sessionsAbsent.toString(),
                ),
            ),
        )

        progress.mentor?.let { mentor ->
            binding.panel.addSection(
                getString(R.string.progress_mentor),
                listOf(
                    RowContent(
                        title = mentor.mentorName,
                        subtitle = mentor.occupation,
                    )
                ),
            )
        }

        binding.panel.addSection(
            getString(R.string.badges_title),
            progress.badges.take(5).map { badge ->
                RowContent(
                    title = badge.name,
                    subtitle = badge.description,
                    pill = "${badge.points}",
                    pillColour = ContextCompat.getColor(requireContext(), R.color.gold),
                )
            },
        ) {
            viewModel.learnerId?.let { id ->
                findNavController().navigate(R.id.toBadges, bundleOf("learnerId" to id))
            }
        }
    }

    override fun onDestroyView() {
        super.onDestroyView()
        _binding = null
    }
}
