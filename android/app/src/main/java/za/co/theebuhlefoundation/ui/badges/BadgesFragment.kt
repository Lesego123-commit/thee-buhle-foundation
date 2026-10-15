// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Google (2026) Android Developers documentation.
//    - Google (2026) Material Design 3.
// =============================================================================

package za.co.theebuhlefoundation.ui.badges

import android.os.Bundle
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import androidx.core.content.ContextCompat
import androidx.fragment.app.Fragment
import androidx.fragment.app.viewModels
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.lifecycleScope
import androidx.lifecycle.repeatOnLifecycle
import kotlinx.coroutines.launch
import za.co.theebuhlefoundation.R
import za.co.theebuhlefoundation.databinding.FragmentBadgesBinding
import za.co.theebuhlefoundation.ui.common.ContainerViewModelFactory
import za.co.theebuhlefoundation.ui.common.Format
import za.co.theebuhlefoundation.ui.common.RowContent
import za.co.theebuhlefoundation.ui.common.StateViews
import za.co.theebuhlefoundation.ui.common.addSection
import za.co.theebuhlefoundation.ui.common.container

class BadgesFragment : Fragment() {

    private var _binding: FragmentBadgesBinding? = null
    private val binding get() = _binding!!

    private lateinit var state: StateViews

    private val viewModel: BadgesViewModel by viewModels {
        ContainerViewModelFactory(container) {
            BadgesViewModel(it.auth, it.learning, it.progress, arguments?.getString("learnerId"))
        }
    }

    override fun onCreateView(
        inflater: LayoutInflater,
        parent: ViewGroup?,
        savedInstanceState: Bundle?,
    ): View {
        _binding = FragmentBadgesBinding.inflate(inflater, parent, false)

        return binding.root
    }

    override fun onViewCreated(view: View, savedInstanceState: Bundle?) {
        state = StateViews(binding.stateHost, binding.content)
        state.onRetry { viewModel.load() }

        viewLifecycleOwner.lifecycleScope.launch {
            viewLifecycleOwner.repeatOnLifecycle(Lifecycle.State.STARTED) {
                viewModel.state.collect { current -> state.render(current, ::render) }
            }
        }

        viewModel.load()
    }

    private fun render(data: BadgesData) {
        binding.milestoneName.text = data.milestone.milestoneName
        binding.milestonePoints.text = getString(
            R.string.badges_points,
            data.milestone.points,
            (data.milestone.nextTarget - data.milestone.points).coerceAtLeast(0),
        )
        binding.milestoneProgress.progress = data.milestone.progressPercent

        binding.nextBadge.text = data.milestone.nextBadge?.let { next ->
            getString(R.string.badges_next_detail, next.name, next.remaining)
        } ?: getString(R.string.badges_all_earned)

        binding.panel.removeAllViews()

        binding.panel.addSection(
            getString(R.string.badges_earned),
            data.earned.map { badge ->
                RowContent(
                    title = badge.name,
                    subtitle = "${badge.description} · ${Format.date(badge.awardedAt)}",
                    pill = badge.points.toString(),
                    pillColour = ContextCompat.getColor(requireContext(), R.color.gold),
                )
            },
        )

        binding.panel.addSection(
            getString(R.string.badges_to_earn),
            data.remaining.map { badge ->
                RowContent(
                    title = badge.name,
                    subtitle = badge.description,
                    pill = getString(R.string.badges_threshold, badge.threshold),
                    pillColour = ContextCompat.getColor(requireContext(), R.color.ink_40),
                )
            },
        )
    }

    override fun onDestroyView() {
        super.onDestroyView()
        _binding = null
    }
}
