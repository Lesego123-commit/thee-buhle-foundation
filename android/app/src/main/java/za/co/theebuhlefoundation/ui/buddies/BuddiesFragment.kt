// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Google (2026) Android Developers documentation.
//    - Google (2026) Material Design 3.
// =============================================================================

package za.co.theebuhlefoundation.ui.buddies

import android.os.Bundle
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import androidx.fragment.app.Fragment
import androidx.fragment.app.viewModels
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.lifecycleScope
import androidx.lifecycle.repeatOnLifecycle
import com.google.android.material.snackbar.Snackbar
import kotlinx.coroutines.launch
import za.co.theebuhlefoundation.R
import za.co.theebuhlefoundation.databinding.FragmentBuddiesBinding
import za.co.theebuhlefoundation.ui.common.ContainerViewModelFactory
import za.co.theebuhlefoundation.ui.common.RowContent
import za.co.theebuhlefoundation.ui.common.StateViews
import za.co.theebuhlefoundation.ui.common.addSection
import za.co.theebuhlefoundation.ui.common.container

class BuddiesFragment : Fragment() {

    private var _binding: FragmentBuddiesBinding? = null
    private val binding get() = _binding!!

    private lateinit var state: StateViews

    private val viewModel: BuddiesViewModel by viewModels {
        ContainerViewModelFactory(container) { BuddiesViewModel(it.peers) }
    }

    override fun onCreateView(
        inflater: LayoutInflater,
        parent: ViewGroup?,
        savedInstanceState: Bundle?,
    ): View {
        _binding = FragmentBuddiesBinding.inflate(inflater, parent, false)

        return binding.root
    }

    override fun onViewCreated(view: View, savedInstanceState: Bundle?) {
        state = StateViews(binding.stateHost, binding.refresh)
        state.onRetry { viewModel.load() }

        binding.refresh.setOnRefreshListener { viewModel.load(refreshing = true) }

        viewLifecycleOwner.lifecycleScope.launch {
            viewLifecycleOwner.repeatOnLifecycle(Lifecycle.State.STARTED) {
                launch {
                    viewModel.state.collect { current ->
                        binding.refresh.isRefreshing = false
                        state.render(current, ::render)
                    }
                }

                launch {
                    viewModel.message.collect { message ->
                        message ?: return@collect

                        Snackbar.make(binding.root, message, Snackbar.LENGTH_LONG).show()
                        viewModel.messageShown()
                    }
                }
            }
        }

        viewModel.load()
    }

    private fun render(data: BuddiesData) {
        binding.panel.removeAllViews()

        binding.panel.addSection(
            getString(R.string.buddies_suggestions),
            data.suggestions.map { suggestion ->
                RowContent(
                    // First name and grade. The API sends no surname here, and
                    // the screen shows what it is given.
                    title = "${suggestion.firstName} · Grade ${suggestion.grade}",
                    subtitle = suggestion.sharedSubjects.joinToString(", "),
                    pill = getString(
                        if (suggestion.learnerId in data.requested) R.string.buddies_requested
                        else R.string.buddies_request
                    ),
                )
            },
        ) { index ->
            val suggestion = data.suggestions[index]

            if (suggestion.learnerId !in data.requested) {
                viewModel.requestBuddy(suggestion.learnerId, suggestion.firstName)
            }
        }

        binding.panel.addSection(
            getString(R.string.buddies_groups),
            data.groups.map { group ->
                RowContent(
                    title = group.name,
                    subtitle = listOfNotNull(group.subjectName, group.meetsAt, group.location)
                        .joinToString(" · "),
                    pill = group.myStatus?.uppercase() ?: getString(R.string.buddies_join),
                )
            },
        ) { index ->
            val group = data.groups[index]

            if (group.myStatus == null) {
                viewModel.joinGroup(group.groupId, group.name)
            }
        }
    }

    override fun onDestroyView() {
        super.onDestroyView()
        _binding = null
    }
}
