// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Google (2026) Android Developers documentation.
//    - Google (2026) Material Design 3.
// =============================================================================

package za.co.theebuhlefoundation.ui.learn

import android.os.Bundle
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import androidx.core.os.bundleOf
import androidx.core.widget.doAfterTextChanged
import androidx.fragment.app.Fragment
import androidx.fragment.app.viewModels
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.lifecycleScope
import androidx.lifecycle.repeatOnLifecycle
import androidx.navigation.fragment.findNavController
import androidx.recyclerview.widget.LinearLayoutManager
import kotlinx.coroutines.launch
import za.co.theebuhlefoundation.R
import za.co.theebuhlefoundation.data.api.dto.SubjectCard
import za.co.theebuhlefoundation.databinding.FragmentLearnBinding
import za.co.theebuhlefoundation.databinding.ItemSubjectBinding
import za.co.theebuhlefoundation.ui.common.BindingAdapter
import za.co.theebuhlefoundation.ui.common.ContainerViewModelFactory
import za.co.theebuhlefoundation.ui.common.StateViews
import za.co.theebuhlefoundation.ui.common.container

class LearnFragment : Fragment() {

    private var _binding: FragmentLearnBinding? = null
    private val binding get() = _binding!!

    private lateinit var state: StateViews

    private val viewModel: LearnViewModel by viewModels {
        ContainerViewModelFactory(container) { LearnViewModel(it.learning) }
    }

    private val adapter = BindingAdapter<SubjectCard, ItemSubjectBinding>(
        inflate = ItemSubjectBinding::inflate,
        identity = { it.subjectId },
    ) { binding, subject, _ ->
        binding.subjectName.text = subject.name
        binding.subjectMeta.text = binding.root.context.getString(
            R.string.subject_meta,
            subject.grade,
            subject.completedCount,
            subject.resourceCount,
        )
        binding.subjectProgress.progress =
            if (subject.resourceCount == 0) 0
            else 100 * subject.completedCount / subject.resourceCount

        binding.root.setOnClickListener {
            findNavController().navigate(
                R.id.toSubject,
                bundleOf("subjectId" to subject.subjectId, "subjectName" to subject.name),
            )
        }
    }

    override fun onCreateView(
        inflater: LayoutInflater,
        parent: ViewGroup?,
        savedInstanceState: Bundle?,
    ): View {
        _binding = FragmentLearnBinding.inflate(inflater, parent, false)

        return binding.root
    }

    override fun onViewCreated(view: View, savedInstanceState: Bundle?) {
        state = StateViews(binding.stateHost, binding.content)
        state.onRetry { viewModel.load() }

        binding.list.layoutManager = LinearLayoutManager(requireContext())
        binding.list.adapter = adapter

        binding.searchInput.doAfterTextChanged { viewModel.search(it?.toString().orEmpty()) }

        viewLifecycleOwner.lifecycleScope.launch {
            viewLifecycleOwner.repeatOnLifecycle(Lifecycle.State.STARTED) {
                viewModel.state.collect { current ->
                    state.render(current) { data -> adapter.submitList(data.subjects) }
                }
            }
        }

        viewModel.load()
    }

    override fun onDestroyView() {
        super.onDestroyView()
        binding.list.adapter = null
        _binding = null
    }
}
