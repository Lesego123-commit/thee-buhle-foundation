// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Google (2026) Android Developers documentation.
//    - Google (2026) Material Design 3.
// =============================================================================

package za.co.theebuhlefoundation.ui.home

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
import androidx.navigation.fragment.findNavController
import com.google.android.material.chip.Chip
import com.google.android.material.dialog.MaterialAlertDialogBuilder
import com.google.android.material.snackbar.Snackbar
import kotlinx.coroutines.launch
import za.co.theebuhlefoundation.R
import za.co.theebuhlefoundation.data.api.dto.*
import za.co.theebuhlefoundation.databinding.CardNextSessionBinding
import za.co.theebuhlefoundation.databinding.CardStatsBinding
import za.co.theebuhlefoundation.databinding.FragmentHomeBinding
import za.co.theebuhlefoundation.ui.MainActivity
import za.co.theebuhlefoundation.ui.common.*

class HomeFragment : Fragment() {

    private var _binding: FragmentHomeBinding? = null
    private val binding get() = _binding!!

    private lateinit var state: StateViews

    private val viewModel: HomeViewModel by viewModels {
        ContainerViewModelFactory(container) { HomeViewModel(it.auth, it.learning, it.mentor, it.progress) }
    }

    override fun onCreateView(
        inflater: LayoutInflater,
        parent: ViewGroup?,
        savedInstanceState: Bundle?,
    ): View {
        _binding = FragmentHomeBinding.inflate(inflater, parent, false)

        return binding.root
    }

    override fun onViewCreated(view: View, savedInstanceState: Bundle?) {
        state = StateViews(binding.stateHost, binding.refresh)
        state.onRetry { viewModel.load() }

        binding.refresh.setOnRefreshListener { viewModel.load(refreshing = true) }

        binding.signOutButton.setOnClickListener { viewModel.signOut() }

        binding.greeting.text = viewModel.displayName.orEmpty()
        binding.roleLabel.text = viewModel.roleLabel.orEmpty()

        viewLifecycleOwner.lifecycleScope.launch {
            viewLifecycleOwner.repeatOnLifecycle(Lifecycle.State.STARTED) {
                launch {
                    viewModel.signedOut.collect { signedOut ->
                        if (!signedOut) return@collect

                        (requireActivity() as MainActivity).applyMenuForRole()

                        findNavController().navigate(
                            R.id.signInFragment,
                            null,
                            androidx.navigation.navOptions {
                                // Everything behind it goes, so the back button
                                // cannot walk into the previous person's data.
                                popUpTo(R.id.nav_graph) { inclusive = true }
                            },
                        )
                    }
                }

                launch {
                    viewModel.message.collect { message ->
                        message ?: return@collect

                        Snackbar.make(binding.root, message, Snackbar.LENGTH_LONG).show()
                        viewModel.messageShown()
                    }
                }

                viewModel.state.collect { current ->
                    binding.refresh.isRefreshing = false

                    state.render(current) { data ->
                        binding.greeting.text = viewModel.displayName.orEmpty()
                        binding.roleLabel.text = viewModel.roleLabel.orEmpty()

                        when (data) {
                            is HomeData.Learner -> renderLearner(data.dashboard)
                            is HomeData.Mentor -> renderMentor(data.dashboard)
                        }
                    }
                }
            }
        }

        viewModel.load()
    }

    private fun renderLearner(dashboard: ParentDashboard) {
        binding.panel.removeAllViews()

        renderChildren(dashboard)

        val panel = dashboard.selected ?: return

        panel.nextSession?.let { session ->
            val card = CardNextSessionBinding.inflate(layoutInflater, binding.panel, false)

            card.sessionFocus.text = session.subjectFocus ?: session.programmeName
            card.sessionWhen.text = Format.sessionWhen(session.startsAt, session.endsAt)
            card.sessionWhere.text = listOfNotNull(session.venueName, session.room).joinToString(", ")

            binding.panel.addView(card.root)
        }

        val stats = CardStatsBinding.inflate(layoutInflater, binding.panel, false)

        stats.attendanceValue.text = Format.percent(panel.attendanceRate)
        stats.attendanceNote.text = getString(
            R.string.home_attendance_note,
            panel.attendance.sessionsAttended,
            panel.attendance.sessionsRecorded,
        )
        stats.badgesValue.text = panel.badgeCount.toString()
        stats.subjectNote.text = panel.headlineSubject?.let {
            "${it.subjectName} ${Format.improvement(it.improvement)}"
        } ?: getString(R.string.home_no_marks)

        binding.panel.addView(stats.root)

        // Permission forms first when there are any: they are the only thing on
        // this screen that needs the parent to do something, and burying them
        // under the attendance history is how a camp deadline gets missed.
        binding.panel.addSection(
            getString(R.string.home_forms_waiting),
            panel.outstandingForms.map { form ->
                RowContent(
                    title = form.title,
                    subtitle = getString(R.string.home_form_due, Format.plainDate(form.dueDate)),
                    pill = form.lastDecision?.uppercase(),
                    pillColour = ContextCompat.getColor(requireContext(), R.color.gold),
                )
            },
        ) { index -> showForm(panel.learnerId, panel.outstandingForms[index]) }

        binding.panel.addSection(
            getString(R.string.home_recent),
            panel.recentAttendance.take(6).map { entry ->
                RowContent(
                    title = entry.subjectFocus ?: entry.programmeName,
                    subtitle = "${Format.dayMonth(entry.startsAt)} · ${entry.venueName.orEmpty()}",
                    pill = entry.status.uppercase(),
                    pillColour = ContextCompat.getColor(
                        requireContext(),
                        when (entry.status) {
                            "Present" -> R.color.forest
                            "Late" -> R.color.gold
                            else -> R.color.clay
                        },
                    ),
                )
            },
        )

        binding.panel.addSection(
            getString(R.string.nav_messages),
            dashboard.inbox.take(4).map { message ->
                RowContent(
                    title = message.title,
                    subtitle = "${message.senderName} · ${Format.date(message.publishedAt)}",
                    pill = if (message.readAt == null) getString(R.string.home_new) else null,
                    pillColour = ContextCompat.getColor(requireContext(), R.color.gold),
                )
            },
        ) { findNavController().navigate(R.id.toMessages) }
    }

    /**
     * The whole form, and the parent's answer to it.
     *
     * Declining is offered as plainly as agreeing: consent that can only be
     * given, never refused, is not consent. "Not now" leaves the form waiting.
     */
    private fun showForm(learnerId: String, form: OutstandingForm) {
        val details = buildString {
            append(form.body)

            form.eventTitle?.let { append("\n\n").append(it) }
            form.eventStartsAt?.let { append("\n").append(Format.longDate(it)).append(", ").append(Format.time(it)) }
            form.eventLocation?.let { append("\n").append(it) }

            append("\n\n").append(getString(R.string.home_form_due, Format.plainDate(form.dueDate)))

            if (!viewModel.canAnswerForms) {
                append("\n\n").append(getString(R.string.form_parent_only))
            }
        }

        val dialog = MaterialAlertDialogBuilder(requireContext())
            .setTitle(form.title)
            .setMessage(details)

        if (viewModel.canAnswerForms) {
            dialog
                .setPositiveButton(R.string.form_grant) { _, _ -> viewModel.answerForm(form.formId, learnerId, granted = true) }
                .setNegativeButton(R.string.form_decline) { _, _ -> viewModel.answerForm(form.formId, learnerId, granted = false) }
                .setNeutralButton(R.string.form_later, null)
        } else {
            dialog.setPositiveButton(android.R.string.ok, null)
        }

        dialog.show()
    }

    /**
     * A parent's children, as chips. A learner signed in on their own account
     * has none, so the row is not drawn at all rather than drawn empty.
     */
    private fun renderChildren(dashboard: ParentDashboard) {
        binding.childrenChips.removeAllViews()

        if (dashboard.children.size <= 1) {
            binding.childrenScroller.visibility = View.GONE

            return
        }

        binding.childrenScroller.visibility = View.VISIBLE

        dashboard.children.forEach { child ->
            val chip = Chip(requireContext()).apply {
                text = child.firstName
                isCheckable = true
                isChecked = child.learnerId == dashboard.selected?.learnerId
                setOnClickListener { viewModel.selectLearner(child.learnerId) }
            }

            binding.childrenChips.addView(chip)
        }
    }

    private fun renderMentor(dashboard: MentorDashboard) {
        binding.panel.removeAllViews()
        binding.childrenScroller.visibility = View.GONE

        binding.panel.addSection(
            getString(R.string.mentees_title),
            dashboard.mentees.map { mentee ->
                RowContent(
                    title = "${mentee.firstName} ${mentee.surname}",
                    subtitle = getString(
                        R.string.mentee_subtitle,
                        mentee.grade,
                        mentee.checkInCount,
                    ),
                    pill = if (mentee.isOverdue) getString(R.string.check_in_overdue) else null,
                    pillColour = ContextCompat.getColor(requireContext(), R.color.clay),
                )
            },
        ) { findNavController().navigate(R.id.menteesFragment) }
    }

    override fun onDestroyView() {
        super.onDestroyView()
        _binding = null
    }
}
